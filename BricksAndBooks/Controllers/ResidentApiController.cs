using System.Security.Claims;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Controllers;

[ApiController]
[Route("api/v1/me")]
public class ResidentApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApiSessionService _sessions;

    public ResidentApiController(AppDbContext db, UserManager<ApplicationUser> users, ApiSessionService sessions)
    { _db = db; _users = users; _sessions = sessions; }

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications()
    {
        if (!TryUser(out var userId)) return Unauthorized();
        var items = await _db.Notifications.Where(n => n.UserId == userId).OrderByDescending(n => n.CreatedAt).Take(100).Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.Title, n.Message, n.IsRead, n.CreatedAt, n.Url, n.RelatedEntityId)).ToListAsync();
        return Ok(items);
    }

    [HttpPost("notifications/{id:int}/read")]
    public async Task<IActionResult> MarkNotificationRead(int id)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        var item = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (item == null) return NotFound();
        item.IsRead = true; item.ReadAt = DateTime.UtcNow; await _db.SaveChangesAsync(); return NoContent();
    }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllNotificationsRead()
    {
        if (!TryUser(out var userId)) return Unauthorized();
        var items = await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
        foreach (var item in items) { item.IsRead = true; item.ReadAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(); return NoContent();
    }

    [HttpGet("vehicles")]
    public async Task<IActionResult> Vehicles()
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return Ok(await _db.Vehicles.Where(v => v.UserId == userId).OrderBy(v => v.RegistrationNumber)
            .Select(v => new VehicleDto(v.Id, v.RegistrationNumber, v.Make, v.Model, v.Colour, v.VehicleType, v.RegisteredAt, v.UpdatedAt))
            .ToListAsync());
    }

    [HttpPost("vehicles")]
    public async Task<IActionResult> CreateVehicle(VehicleRequest request)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        var registration = Normalize(request.RegistrationNumber);
        if (string.IsNullOrWhiteSpace(registration) || registration.Length > 20 || string.IsNullOrWhiteSpace(request.Make) || string.IsNullOrWhiteSpace(request.Model) || string.IsNullOrWhiteSpace(request.Colour) || string.IsNullOrWhiteSpace(request.VehicleType)) return BadRequest(new { message = "Registration, make, model, colour, and vehicle type are required." });
        if (await _db.Vehicles.AnyAsync(v => v.RegistrationNumber == registration)) return Conflict(new { message = "This vehicle registration number is already registered." });
        var vehicle = new Vehicle { UserId = userId, RegistrationNumber = registration, Make = request.Make.Trim(), Model = request.Model.Trim(), Colour = request.Colour.Trim(), VehicleType = request.VehicleType.Trim() };
        _db.Vehicles.Add(vehicle); await _db.SaveChangesAsync(); return CreatedAtAction(nameof(Vehicles), new { id = vehicle.Id }, ToVehicle(vehicle));
    }

    [HttpPut("vehicles/{id:int}")]
    public async Task<IActionResult> UpdateVehicle(int id, VehicleRequest request)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId); if (vehicle == null) return NotFound();
        var registration = Normalize(request.RegistrationNumber);
        if (string.IsNullOrWhiteSpace(registration) || registration.Length > 20 || await _db.Vehicles.AnyAsync(v => v.Id != id && v.RegistrationNumber == registration)) return BadRequest(new { message = "Enter a valid unique registration number." });
        vehicle.RegistrationNumber = registration; vehicle.Make = request.Make.Trim(); vehicle.Model = request.Model.Trim(); vehicle.Colour = request.Colour.Trim(); vehicle.VehicleType = request.VehicleType.Trim(); vehicle.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); return Ok(ToVehicle(vehicle));
    }

    [HttpDelete("vehicles/{id:int}")]
    public async Task<IActionResult> DeleteVehicle(int id)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        var vehicle = await _db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId); if (vehicle == null) return NotFound();
        _db.Vehicles.Remove(vehicle); await _db.SaveChangesAsync(); return NoContent();
    }

    [HttpGet("maintenance")]
    public async Task<IActionResult> Maintenance()
    {
        if (!TryUser(out var userId, out var user)) return Unauthorized();
        var query = _db.MaintenanceRequests.Include(r => r.Unit).Include(r => r.AssignedToUser).AsQueryable();
        var roles = await _users.GetRolesAsync(user!);
        if (roles.Contains("MaintenanceWorker")) query = query.Where(r => r.AssignedToUserId == userId);
        else if (!roles.Contains("Admin") && !roles.Contains("FinancialOfficer")) query = query.Where(r => r.UserId == userId);
        var items = await query.OrderByDescending(r => r.SubmittedAt).Take(100).Select(r => new MaintenanceDto(r.Id, r.UnitId, r.Category.ToString(), r.Description, r.Status.ToString(), r.SubmittedAt, r.UpdatedAt, r.AssignedToUser == null ? null : (r.AssignedToUser.FirstName + " " + r.AssignedToUser.LastName).Trim(), r.AttachmentUrls)).ToListAsync();
        return Ok(items);
    }

    [HttpPost("maintenance")]
    public async Task<IActionResult> CreateMaintenance(MaintenanceRequestDto request)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        if (request.UnitId <= 0 || string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length > 2000) return BadRequest(new { message = "Choose your allocated unit and describe the issue in 2,000 characters or fewer." });
        var ownsUnit = await _db.Occupancies.AnyAsync(o => o.UserId == userId && o.UnitId == request.UnitId && (o.EndDate == null || o.EndDate >= DateTime.UtcNow));
        if (!ownsUnit) return Forbid();
        if (!Enum.TryParse<MaintenanceCategory>(request.Category, true, out var category)) return BadRequest(new { message = "Choose a valid maintenance category." });
        var item = new MaintenanceRequest { UserId = userId, UnitId = request.UnitId, Category = category, Description = request.Description.Trim(), Status = MaintenanceStatus.Submitted };
        _db.MaintenanceRequests.Add(item); await _db.SaveChangesAsync(); return CreatedAtAction(nameof(Maintenance), new { id = item.Id }, new { id = item.Id, message = "Maintenance request submitted." });
    }

    [HttpGet("reviews")]
    public async Task<IActionResult> Reviews()
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return Ok(await _db.Reviews.Include(r => r.Accommodation).Where(r => r.UserId == userId || r.IsApproved).OrderByDescending(r => r.CreatedAt).Take(100).Select(r => new ReviewDto(r.Id, r.AccommodationId, r.Accommodation.Name, r.Rating, r.Title, r.Comment, r.IsApproved, r.CreatedAt)).ToListAsync());
    }

    [HttpPost("reviews")]
    public async Task<IActionResult> CreateReview(ReviewRequest request)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        if (request.Rating < 1 || request.Rating > 5 || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200 || string.IsNullOrWhiteSpace(request.Comment) || request.Comment.Trim().Length > 2000) return BadRequest(new { message = "Choose a rating from 1 to 5 and provide a valid title and comment." });
        if (!await _db.Accommodations.AnyAsync(a => a.Id == request.AccommodationId)) return NotFound(new { message = "Residence not found." });
        var item = new Review { UserId = userId, AccommodationId = request.AccommodationId, Rating = request.Rating, Title = request.Title.Trim(), Comment = request.Comment.Trim(), IsApproved = true };
        _db.Reviews.Add(item); await _db.SaveChangesAsync(); return CreatedAtAction(nameof(Reviews), new { id = item.Id }, new { id = item.Id, message = "Review posted." });
    }

    [HttpGet("chat")]
    public async Task<IActionResult> Chat(string? contactId = null)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        var messages = await _db.ChatMessages.Include(m => m.Sender).Include(m => m.Recipient).Where(m => m.SenderUserId == userId || m.RecipientUserId == userId).OrderBy(m => m.SentAt).Take(500).Select(m => new ChatDto(m.Id, m.SenderUserId, (m.Sender.FirstName + " " + m.Sender.LastName).Trim(), m.RecipientUserId, (m.Recipient.FirstName + " " + m.Recipient.LastName).Trim(), m.Content, m.IsRead, m.SentAt)).ToListAsync();
        if (!string.IsNullOrWhiteSpace(contactId)) messages = messages.Where(m => (m.SenderUserId == userId && m.RecipientUserId == contactId) || (m.SenderUserId == contactId && m.RecipientUserId == userId)).ToList();
        return Ok(messages);
    }

    [HttpPost("chat")]
    public async Task<IActionResult> SendChat(ChatRequest request)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.RecipientUserId) || request.RecipientUserId == userId || string.IsNullOrWhiteSpace(request.Content) || request.Content.Trim().Length > 2000) return BadRequest(new { message = "Choose a recipient and enter a message of 2,000 characters or fewer." });
        if (await _users.FindByIdAsync(request.RecipientUserId) == null) return NotFound(new { message = "Recipient not found." });
        var item = new ChatMessage { SenderUserId = userId, RecipientUserId = request.RecipientUserId, Content = request.Content.Trim(), SentAt = DateTime.UtcNow };
        _db.ChatMessages.Add(item); await _db.SaveChangesAsync(); return CreatedAtAction(nameof(Chat), new { id = item.Id }, new { id = item.Id, message = "Message sent." });
    }

    private bool TryUser(out string userId) => TryUser(out userId, out _);
    private bool TryUser(out string userId, out ApplicationUser? user)
    { userId = string.Empty; user = null; var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim(); if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetUserId(token, out userId)) return false; user = _users.FindByIdAsync(userId).GetAwaiter().GetResult(); return user != null; }
    private static string Normalize(string? value) => string.Concat((value ?? string.Empty).Where(char.IsLetterOrDigit)).ToUpperInvariant();
    private static VehicleDto ToVehicle(Vehicle v) => new(v.Id, v.RegistrationNumber, v.Make, v.Model, v.Colour, v.VehicleType, v.RegisteredAt, v.UpdatedAt);
    public record NotificationDto(int Id, string Type, string Title, string Message, bool IsRead, DateTime CreatedAt, string? Url, int? RelatedEntityId);
    public record VehicleRequest(string RegistrationNumber, string Make, string Model, string Colour, string VehicleType);
    public record VehicleDto(int Id, string RegistrationNumber, string Make, string Model, string Colour, string VehicleType, DateTime RegisteredAt, DateTime UpdatedAt);
    public record MaintenanceRequestDto(int UnitId, string Category, string Description);
    public record MaintenanceDto(int Id, int UnitId, string Category, string Description, string Status, DateTime SubmittedAt, DateTime? UpdatedAt, string? AssignedTo, string? AttachmentUrls);
    public record ReviewRequest(int AccommodationId, int Rating, string Title, string Comment);
    public record ReviewDto(int Id, int AccommodationId, string AccommodationName, int Rating, string Title, string Comment, bool IsApproved, DateTime CreatedAt);
    public record ChatRequest(string RecipientUserId, string Content);
    public record ChatDto(int Id, string SenderUserId, string SenderName, string RecipientUserId, string RecipientName, string Content, bool IsRead, DateTime SentAt);
}
