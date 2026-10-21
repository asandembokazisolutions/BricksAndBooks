using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Controllers;

[ApiController]
[Route("api/v1")]
public class MobileApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApiSessionService _sessions;
    private readonly TariffCalculationService _tariffs;

    public MobileApiController(AppDbContext db, UserManager<ApplicationUser> users, ApiSessionService sessions, TariffCalculationService tariffs)
    {
        _db = db; _users = users; _sessions = sessions; _tariffs = tariffs;
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", service = "BricksAndBooks API", version = "v1" });

    [HttpPost("auth/login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Email and password are required." });
        var user = await _users.FindByEmailAsync(request.Email.Trim());
        if (user == null || !(await _users.CheckPasswordAsync(user, request.Password)))
            return Unauthorized(new { message = "Invalid email or password." });
        return Ok(new AuthResponse(_sessions.Create(user.Id), ToUser(user, await _users.GetRolesAsync(user))));
    }

    [HttpPost("auth/register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new { message = "Email, first name, and last name are required." });
        if (request.IsStudent == request.IsStaff)
            return BadRequest(new { message = "Select exactly one account type: student or staff." });
        var studentNumber = request.IsStudent ? request.StudentNumber?.Trim() : null;
        var employeeNumber = request.IsStaff ? request.EmployeeNumber?.Trim() : null;
        if (request.IsStudent && !UniversityAccountAddress.IsValidStudentNumber(studentNumber))
            return BadRequest(new { message = "Student number must contain exactly 10 digits." });
        if (request.IsStaff && !UniversityAccountAddress.IsValidEmployeeNumber(employeeNumber))
            return BadRequest(new { message = "Employee number must contain exactly 6 digits." });
        var personalEmail = request.Email.Trim().ToLowerInvariant();
        if (personalEmail.Length > 150 || !new EmailAddressAttribute().IsValid(personalEmail))
            return BadRequest(new { message = "Enter a valid personal contact email address." });
        var loginAddress = request.IsStudent
            ? UniversityAccountAddress.ForStudent(studentNumber!)
            : UniversityAccountAddress.ForStaff(employeeNumber!);
        var user = new ApplicationUser { UserName = loginAddress, Email = loginAddress, PersonalEmail = personalEmail, FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(), StudentNumber = studentNumber, EmployeeNumber = employeeNumber, IsStudent = request.IsStudent, IsStaff = request.IsStaff };
        var result = await _users.CreateAsync(user, request.Password ?? string.Empty);
        if (!result.Succeeded) return BadRequest(new { message = string.Join(" ", result.Errors.Select(error => error.Description)) });
        var role = request.IsStaff ? "Staff" : "Student";
        await _users.AddToRoleAsync(user, role);
        return Ok(new AuthResponse(_sessions.Create(user.Id), ToUser(user, new[] { role })));
    }

    [HttpPost("auth/logout")]
    public IActionResult Logout() { if (TryGetSession(out _, out var token)) _sessions.Revoke(token); return NoContent(); }

    [HttpGet("accommodations")]
    public async Task<IActionResult> Accommodations()
    {
        var accommodations = await _db.Accommodations.Include(a => a.Units).OrderBy(a => a.Name).ToListAsync();
        var activeOccupancies = await _db.Occupancies.Where(o => o.EndDate == null || o.EndDate > DateTime.UtcNow).GroupBy(o => o.UnitId).Select(g => new { UnitId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.UnitId, x => x.Count);
        return Ok(accommodations.Select(a => new AccommodationDto(a.Id, a.Name, a.Address, a.Description, a.ImageUrls, a.Units.Count, a.Units.Sum(u => u.Capacity), a.Units.Sum(u => u.Capacity) - a.Units.Sum(u => activeOccupancies.GetValueOrDefault(u.Id)))));
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me() { if (!TryGetSession(out var userId, out _)) return Unauthorized(); var user = await _users.FindByIdAsync(userId); return user == null ? Unauthorized() : Ok(ToUser(user, await _users.GetRolesAsync(user))); }

    [HttpGet("me/dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        if (!TryGetSession(out var userId, out _)) return Unauthorized();
        var user = await _users.FindByIdAsync(userId); if (user == null) return Unauthorized();
        var applications = await _db.AccommodationApplications.Include(a => a.Accommodation).Include(a => a.AssignedUnit).Where(a => a.UserId == userId).OrderByDescending(a => a.CreatedAt).ToListAsync();
        var unitIds = await _db.Occupancies.Where(o => o.UserId == userId && (o.EndDate == null || o.EndDate > DateTime.UtcNow)).Select(o => o.UnitId).ToListAsync();
        var bills = await _db.Bills.Where(b => unitIds.Contains(b.UnitId)).OrderByDescending(b => b.Year).ThenByDescending(b => b.Month).Take(12).ToListAsync();
        var openBills = bills.Where(b => b.Status != BillStatus.Paid && b.Status != BillStatus.Cancelled).ToList();
        return Ok(new { user = ToUser(user, await _users.GetRolesAsync(user)), applications = applications.Select(ToApplication), bills = bills.Select(ToBill), outstandingBalance = openBills.Sum(b => b.TotalAmount), accommodation = applications.FirstOrDefault(a => a.Status == AccommodationApplicationStatus.Approved)?.Accommodation?.Name, unit = applications.FirstOrDefault(a => a.Status == AccommodationApplicationStatus.Approved)?.AssignedUnit?.UnitNumber });
    }

    [HttpGet("me/applications")]
    public async Task<IActionResult> Applications() { if (!TryGetSession(out var userId, out _)) return Unauthorized(); var items = await _db.AccommodationApplications.Include(a => a.Accommodation).Include(a => a.AssignedUnit).Where(a => a.UserId == userId).OrderByDescending(a => a.CreatedAt).ToListAsync(); return Ok(items.Select(ToApplication)); }

    [HttpPost("me/applications")]
    public async Task<IActionResult> Apply(ApplicationRequest request)
    {
        if (!TryGetSession(out var userId, out _)) return Unauthorized();
        var accommodation = await _db.Accommodations.Include(a => a.Units).FirstOrDefaultAsync(a => a.Id == request.AccommodationId);
        if (accommodation == null) return NotFound(new { message = "Residence not found." });
        var occupied = await _db.Occupancies.Where(o => o.EndDate == null || o.EndDate > DateTime.UtcNow).Select(o => o.UnitId).ToListAsync();
        if (accommodation.Units.Sum(u => u.Capacity) - occupied.Count(id => accommodation.Units.Any(u => u.Id == id)) <= 0) return BadRequest(new { message = "This residence has no available space." });
        var application = new AccommodationApplication { UserId = userId, AccommodationId = request.AccommodationId, ApplicantMessage = request.Message?.Trim(), Status = AccommodationApplicationStatus.Pending, CreatedAt = DateTime.UtcNow };
        _db.AccommodationApplications.Add(application); await _db.SaveChangesAsync(); return CreatedAtAction(nameof(Applications), new { id = application.Id }, new { id = application.Id, message = "Application submitted." });
    }

    [HttpGet("me/bills")]
    public async Task<IActionResult> Bills() { if (!TryGetSession(out var userId, out _)) return Unauthorized(); var unitIds = await _db.Occupancies.Where(o => o.UserId == userId && (o.EndDate == null || o.EndDate > DateTime.UtcNow)).Select(o => o.UnitId).ToListAsync(); var bills = await _db.Bills.Where(b => unitIds.Contains(b.UnitId)).OrderByDescending(b => b.Year).ThenByDescending(b => b.Month).ToListAsync(); return Ok(bills.Select(ToBill)); }

    [HttpGet("me/cards")]
    public async Task<IActionResult> Cards() { if (!TryGetSession(out var userId, out _)) return Unauthorized(); return Ok(await _db.SavedPaymentCards.Where(c => c.UserId == userId).OrderByDescending(c => c.CreatedAt).Select(c => new CardDto(c.Id, c.CardholderName, c.LastFour, c.Expiry)).ToListAsync()); }

    private bool TryGetSession(out string userId, out string token)
    {
        userId = string.Empty; token = Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
        return !string.IsNullOrWhiteSpace(token) && _sessions.TryGetUserId(token, out userId);
    }
    private static UserDto ToUser(ApplicationUser user, IEnumerable<string> roles) => new(user.Id, user.Email ?? "", user.PersonalEmail ?? "", user.FirstName ?? "", user.LastName ?? "", roles.ToArray());
    private static ApplicationDto ToApplication(AccommodationApplication a) => new(a.Id, a.AccommodationId, a.Accommodation?.Name ?? "Residence", a.Status.ToString(), a.ApplicantMessage, a.AssignedUnit?.UnitNumber, a.CreatedAt);
    private static BillDto ToBill(Bill b) => new(b.Id, b.Year, b.Month, b.RentAmount, b.WaterAmount, b.ElectricityAmount, b.ParkingAmount, b.AdditionalVehicleAmount, b.TotalAmount, b.Status.ToString(), b.DueDate);

    public record LoginRequest(string Email, string Password);
    public record RegisterRequest(string Email, string FirstName, string LastName, string? Password, string? StudentNumber, string? EmployeeNumber, bool IsStudent, bool IsStaff);
    public record ApplicationRequest(int AccommodationId, string? Message);
    public record AuthResponse(string Token, UserDto User);
    public record UserDto(string Id, string Email, string PersonalEmail, string FirstName, string LastName, string[] Roles);
    public record AccommodationDto(int Id, string Name, string Address, string? Description, string? ImageUrls, int UnitCount, int Capacity, int AvailableSpaces);
    public record ApplicationDto(int Id, int AccommodationId, string AccommodationName, string Status, string? Message, string? AssignedUnit, DateTime CreatedAt);
    public record BillDto(int Id, int Year, int Month, decimal Rent, decimal Water, decimal Electricity, decimal Parking, decimal AdditionalVehicle, decimal Total, string Status, DateTime DueDate);
    public record CardDto(int Id, string CardholderName, string LastFour, string Expiry);
}
