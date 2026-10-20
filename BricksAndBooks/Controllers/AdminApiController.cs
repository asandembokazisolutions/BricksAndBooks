using System.Globalization;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Controllers;

/// <summary>
/// Administrative mobile/API surface. Authentication deliberately uses the application's
/// bearer session service rather than the MVC cookie authentication scheme.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
public class AdminApiController : ControllerBase
{
    private static readonly SemaphoreSlim AllocationGate = new(1, 1);

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApiSessionService _sessions;
    private readonly TariffCalculationService _tariffs;
    private readonly NotificationService _notifications;
    private readonly ILogger<AdminApiController> _logger;

    public AdminApiController(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        ApiSessionService sessions,
        TariffCalculationService tariffs,
        NotificationService notifications,
        ILogger<AdminApiController> logger)
    {
        _db = db;
        _users = users;
        _sessions = sessions;
        _tariffs = tariffs;
        _notifications = notifications;
        _logger = logger;
    }

    [HttpGet("summary")]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Summary()
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;

        // Keep the dashboard figures aligned with AdminController.Index.
        var totalAccommodations = await _db.Accommodations.CountAsync();
        var totalUnits = await _db.Units.CountAsync();
        var totalBills = await _db.Bills.CountAsync();
        var totalPayments = await _db.Payments.CountAsync();
        var pendingPayments = await _db.Payments.CountAsync(payment => payment.Status == PaymentStatus.Pending);
        var totalUsers = await _users.Users.CountAsync();
        var students = await _users.GetUsersInRoleAsync("Student");
        var staff = await _users.GetUsersInRoleAsync("Staff");
        var financialOfficers = await _users.GetUsersInRoleAsync("FinancialOfficer");
        var occupiedUnits = await _db.Units.CountAsync(unit => !unit.IsAvailable);
        var totalRevenue = await _db.Payments
            .Where(payment => payment.Status == PaymentStatus.Successful)
            .SumAsync(payment => (decimal?)payment.Amount) ?? 0m;
        var outstandingBalance = await _db.Bills
            .Where(bill => bill.Status == BillStatus.Pending)
            .SumAsync(bill => (decimal?)(bill.RentAmount + bill.WaterAmount + bill.ElectricityAmount + bill.ParkingAmount + bill.AdditionalVehicleAmount)) ?? 0m;

        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
        var revenue = (await _db.Payments
                .Where(payment => payment.Status == PaymentStatus.Successful && payment.PaymentDate >= sixMonthsAgo)
                .ToListAsync())
            .GroupBy(payment => new { payment.PaymentDate.Year, payment.PaymentDate.Month })
            .OrderBy(group => group.Key.Year)
            .ThenBy(group => group.Key.Month)
            .Select(group => new RevenuePointDto(
                group.Key.Year,
                group.Key.Month,
                $"{CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(group.Key.Month)} {group.Key.Year}",
                group.Sum(payment => payment.Amount)))
            .ToList();

        var occupancy = await _db.Accommodations
            .Include(accommodation => accommodation.Units)
            .OrderBy(accommodation => accommodation.Name)
            .Select(accommodation => new OccupancyPointDto(
                accommodation.Id,
                accommodation.Name,
                accommodation.Units.Count,
                accommodation.Units.Count(unit => !unit.IsAvailable),
                accommodation.Units.Count == 0
                    ? 0d
                    : (double)accommodation.Units.Count(unit => !unit.IsAvailable) / accommodation.Units.Count * 100d))
            .ToListAsync();

        return Ok(new DashboardSummaryDto(
            totalAccommodations,
            totalUnits,
            totalBills,
            totalPayments,
            pendingPayments,
            totalUsers,
            students.Count,
            staff.Count,
            financialOfficers.Count,
            occupiedUnits,
            totalUnits - occupiedUnits,
            totalRevenue,
            outstandingBalance,
            revenue,
            occupancy));
    }

    [HttpGet("accommodations")]
    public async Task<IActionResult> ListAccommodations()
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;

        var items = await _db.Accommodations
            .Include(accommodation => accommodation.Units)
            .OrderBy(accommodation => accommodation.Name)
            .ToListAsync();
        return Ok(items.Select(ToAccommodation));
    }

    [HttpPost("accommodations")]
    public async Task<IActionResult> CreateAccommodation(AccommodationWriteRequest request)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;
        if (!TryValidateAccommodation(request, out var error)) return BadRequest(new { message = error });

        var accommodation = new Accommodation
        {
            Name = request.Name!.Trim(),
            Address = request.Address!.Trim(),
            Description = TrimOrNull(request.Description),
            ImageUrls = TrimOrNull(request.ImageUrls)
        };
        _db.Accommodations.Add(accommodation);
        await _db.SaveChangesAsync();

        return Created($"/api/v1/admin/accommodations/{accommodation.Id}", ToAccommodation(accommodation));
    }

    [HttpPut("accommodations/{id:int}")]
    public async Task<IActionResult> UpdateAccommodation(int id, AccommodationWriteRequest request)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;
        if (id <= 0) return NotFound();
        if (!TryValidateAccommodation(request, out var error)) return BadRequest(new { message = error });

        var accommodation = await _db.Accommodations.Include(item => item.Units).FirstOrDefaultAsync(item => item.Id == id);
        if (accommodation == null) return NotFound();

        // MVC editing appends successfully uploaded images rather than replacing its gallery.
        accommodation.Name = request.Name!.Trim();
        accommodation.Address = request.Address!.Trim();
        accommodation.Description = TrimOrNull(request.Description);
        var newImageUrls = TrimOrNull(request.ImageUrls);
        if (!string.IsNullOrWhiteSpace(newImageUrls))
        {
            accommodation.ImageUrls = string.Join('|', new[] { accommodation.ImageUrls, newImageUrls }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        accommodation.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(ToAccommodation(accommodation));
    }

    [HttpDelete("accommodations/{id:int}")]
    public async Task<IActionResult> DeleteAccommodation(int id)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;

        // This mirrors AccommodationController.DeleteConfirmed, including its explicit
        // removal of restrictive dependent records and preservation of payment history.
        var accommodation = await _db.Accommodations.FirstOrDefaultAsync(item => item.Id == id);
        if (accommodation == null) return NotFound();

        var unitIds = await _db.Units.Where(unit => unit.AccommodationId == id).Select(unit => unit.Id).ToListAsync();
        var applications = await _db.AccommodationApplications.Where(application => application.AccommodationId == id).ToListAsync();
        var occupancies = await _db.Occupancies.Where(occupancy => unitIds.Contains(occupancy.UnitId)).ToListAsync();
        var maintenanceRequests = await _db.MaintenanceRequests.Where(request => unitIds.Contains(request.UnitId)).ToListAsync();
        var bills = await _db.Bills.Where(bill => unitIds.Contains(bill.UnitId)).ToListAsync();
        var billIds = bills.Select(bill => bill.Id).ToHashSet();
        var allocations = await _db.PaymentAllocations.Where(allocation => billIds.Contains(allocation.BillId)).ToListAsync();
        var paymentIds = allocations.Select(allocation => allocation.PaymentId).ToHashSet();
        var payments = await _db.Payments
            .Where(payment => paymentIds.Contains(payment.Id) || (payment.BillId.HasValue && billIds.Contains(payment.BillId.Value)))
            .ToListAsync();
        foreach (var payment in payments)
        {
            if (payment.BillId.HasValue && billIds.Contains(payment.BillId.Value)) payment.BillId = null;
        }

        _db.PaymentAllocations.RemoveRange(allocations);
        _db.Bills.RemoveRange(bills);
        _db.Occupancies.RemoveRange(occupancies);
        _db.MaintenanceRequests.RemoveRange(maintenanceRequests);
        _db.AccommodationApplications.RemoveRange(applications);
        _db.Units.RemoveRange(_db.Units.Where(unit => unit.AccommodationId == id));
        _db.Accommodations.Remove(accommodation);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("units")]
    public async Task<IActionResult> ListUnits([FromQuery] int? accommodationId)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;

        IQueryable<Unit> query = _db.Units.Include(unit => unit.Accommodation);
        if (accommodationId.HasValue)
        {
            if (accommodationId.Value <= 0) return BadRequest(new { message = "Accommodation id must be valid." });
            if (!await _db.Accommodations.AnyAsync(accommodation => accommodation.Id == accommodationId.Value)) return NotFound(new { message = "Accommodation not found." });
            query = query.Where(unit => unit.AccommodationId == accommodationId.Value);
        }

        var units = await query.OrderBy(unit => unit.Accommodation!.Name).ThenBy(unit => unit.UnitNumber).ToListAsync();
        return Ok(units.Select(ToUnit));
    }

    [HttpPost("units")]
    public async Task<IActionResult> CreateUnit(UnitWriteRequest request)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;
        if (!TryValidateUnit(request, out var error)) return BadRequest(new { message = error });

        var accommodation = await _db.Accommodations.FirstOrDefaultAsync(item => item.Id == request.AccommodationId);
        if (accommodation == null) return NotFound(new { message = "Accommodation not found." });

        // New units are available by default, exactly as UnitController.Create does.
        var unit = new Unit
        {
            UnitNumber = request.UnitNumber!.Trim(),
            AccommodationId = request.AccommodationId,
            Capacity = request.Capacity,
            OccupancyType = request.OccupancyType,
            IsAvailable = true
        };
        _db.Units.Add(unit);
        await _db.SaveChangesAsync();
        unit.Accommodation = accommodation;

        return Created($"/api/v1/admin/units/{unit.Id}", ToUnit(unit));
    }

    [HttpPut("units/{id:int}")]
    public async Task<IActionResult> UpdateUnit(int id, UnitWriteRequest request)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;
        if (id <= 0) return NotFound();
        if (!TryValidateUnit(request, out var error)) return BadRequest(new { message = error });

        var unit = await _db.Units.Include(item => item.Accommodation).FirstOrDefaultAsync(item => item.Id == id);
        if (unit == null) return NotFound();

        // The MVC edit action does not move a unit between residences.
        if (request.AccommodationId != unit.AccommodationId)
            return BadRequest(new { message = "A unit cannot be moved to another accommodation." });

        unit.UnitNumber = request.UnitNumber!.Trim();
        unit.Capacity = request.Capacity;
        unit.OccupancyType = request.OccupancyType;
        unit.IsAvailable = request.IsAvailable;
        unit.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(ToUnit(unit));
    }

    [HttpDelete("units/{id:int}")]
    public async Task<IActionResult> DeleteUnit(int id)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;

        // This matches UnitController.DeleteConfirmed: active residents protect a unit,
        // but historical occupancy and billing records are removed on a permitted delete.
        var unit = await _db.Units.FirstOrDefaultAsync(item => item.Id == id);
        if (unit == null) return NotFound();

        var hasActiveResidents = await _db.Occupancies.AnyAsync(occupancy =>
            occupancy.UnitId == id && (occupancy.EndDate == null || occupancy.EndDate > DateTime.UtcNow));
        if (hasActiveResidents)
            return BadRequest(new { message = "This unit cannot be deleted while it has active residents." });

        var assignedApplications = await _db.AccommodationApplications
            .Where(application => application.AssignedUnitId == id)
            .ToListAsync();
        foreach (var application in assignedApplications) application.AssignedUnitId = null;

        var bills = await _db.Bills.Where(bill => bill.UnitId == id).ToListAsync();
        var maintenanceRequests = await _db.MaintenanceRequests.Where(request => request.UnitId == id).ToListAsync();
        var billIds = bills.Select(bill => bill.Id).ToHashSet();
        var allocations = await _db.PaymentAllocations.Where(allocation => billIds.Contains(allocation.BillId)).ToListAsync();
        var paymentIds = allocations.Select(allocation => allocation.PaymentId).ToHashSet();
        var payments = await _db.Payments.Where(payment => paymentIds.Contains(payment.Id)).ToListAsync();
        foreach (var payment in payments)
        {
            if (payment.BillId.HasValue && billIds.Contains(payment.BillId.Value)) payment.BillId = null;
        }

        _db.PaymentAllocations.RemoveRange(allocations);
        _db.Bills.RemoveRange(bills);
        _db.MaintenanceRequests.RemoveRange(maintenanceRequests);
        _db.Occupancies.RemoveRange(_db.Occupancies.Where(occupancy => occupancy.UnitId == id));
        _db.Units.Remove(unit);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("applications")]
    public async Task<IActionResult> ListApplications()
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;

        var applications = await _db.AccommodationApplications
            .Include(application => application.User)
            .Include(application => application.Accommodation)
            .Include(application => application.AssignedUnit)
            .OrderByDescending(application => application.CreatedAt)
            .ToListAsync();
        return Ok(applications.Select(application => ToApplication(application)));
    }

    [HttpPost("applications/{id:int}/approve")]
    public async Task<IActionResult> ApproveApplication(int id, ApplicationDecisionRequest request)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;
        if (request.AssignedUnitId <= 0) return BadRequest(new { message = "Select a unit belonging to the requested accommodation." });
        if (request.AdminNotes?.Length > 2000) return BadRequest(new { message = "Admin notes must not exceed 2,000 characters." });

        var application = await _db.AccommodationApplications
            .Include(item => item.User)
            .Include(item => item.Accommodation)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (application == null) return NotFound();
        if (application.Status != AccommodationApplicationStatus.Pending)
            return BadRequest(new { message = "Only pending applications can be approved." });

        var hasActiveAccommodation = await _db.Occupancies.AnyAsync(occupancy =>
            occupancy.UserId == application.UserId && (occupancy.EndDate == null || occupancy.EndDate > DateTime.UtcNow));
        if (hasActiveAccommodation)
            return BadRequest(new { message = "This applicant already has an active accommodation." });

        var unit = await _db.Units.FirstOrDefaultAsync(item =>
            item.Id == request.AssignedUnitId && item.AccommodationId == application.AccommodationId);
        if (unit == null)
            return BadRequest(new { message = "Select a unit belonging to the requested accommodation." });

        string assignedBedNumber;
        await AllocationGate.WaitAsync();
        try
        {
            // This is the same process-level race protection used by the MVC approval path.
            var activeResidents = await _db.Occupancies.CountAsync(occupancy =>
                occupancy.UnitId == unit.Id && (occupancy.EndDate == null || occupancy.EndDate > DateTime.UtcNow));
            if (activeResidents >= unit.Capacity || (unit.OccupancyType == UnitOccupancyType.Single && activeResidents > 0))
            {
                _logger.LogWarning("Accommodation approval rejected because unit {UnitId} is full. Application {ApplicationId}.", unit.Id, application.Id);
                return BadRequest(new { message = "The selected unit is no longer available." });
            }

            var occupiedBeds = await _db.Occupancies
                .Where(occupancy => occupancy.UnitId == unit.Id && (occupancy.EndDate == null || occupancy.EndDate > DateTime.UtcNow))
                .Select(occupancy => occupancy.BedNumber)
                .ToListAsync();
            assignedBedNumber = Enumerable.Range(1, unit.Capacity)
                .Select(number => $"Bed {number}")
                .FirstOrDefault(candidate => !occupiedBeds.Contains(candidate)) ?? $"Bed {activeResidents + 1}";

            _db.Occupancies.Add(new Occupancy
            {
                UserId = application.UserId,
                UnitId = unit.Id,
                BedNumber = assignedBedNumber,
                StartDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            application.AssignedUnitId = unit.Id;
            application.Status = AccommodationApplicationStatus.Approved;
            application.AdminNotes = TrimOrNull(request.AdminNotes);
            application.ReviewedAt = DateTime.UtcNow;
            unit.IsAvailable = activeResidents + 1 < unit.Capacity;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Accommodation application {ApplicationId} approved for unit {UnitId}, bed {BedNumber}.", application.Id, unit.Id, assignedBedNumber);
        }
        finally
        {
            AllocationGate.Release();
        }

        // Preserve the MVC approval side effects: issue the current bill, ensure a bill
        // cannot be marked paid absent successful payments, then notify the applicant.
        var approvalDate = DateTime.UtcNow;
        _tariffs.CalculateBills(approvalDate.Year, approvalDate.Month);
        var firstBill = await _db.Bills
            .Where(bill => bill.UnitId == unit.Id && bill.Year == approvalDate.Year && bill.Month == approvalDate.Month)
            .OrderByDescending(bill => bill.CreatedAt)
            .FirstOrDefaultAsync();
        if (firstBill != null)
        {
            var successfulAllocatedAmount = await _db.PaymentAllocations
                .Where(allocation => allocation.BillId == firstBill.Id && allocation.Payment.Status == PaymentStatus.Successful)
                .SumAsync(allocation => (decimal?)allocation.Amount) ?? 0m;
            var successfulLegacyAmount = await _db.Payments
                .Where(payment => payment.BillId == firstBill.Id && !payment.Allocations.Any() && payment.Status == PaymentStatus.Successful)
                .SumAsync(payment => (decimal?)payment.Amount) ?? 0m;
            var successfulAmount = successfulAllocatedAmount + successfulLegacyAmount;
            firstBill.Status = successfulAmount >= firstBill.TotalAmount && firstBill.TotalAmount > 0m
                ? BillStatus.Paid
                : BillStatus.Pending;
            firstBill.UpdatedAt = approvalDate;
            await _db.SaveChangesAsync();
        }

        _notifications.CreateActivityNotification(
            application.UserId,
            "Accommodation application approved",
            $"Your application for {application.Accommodation?.Name ?? "accommodation"} was approved. You were assigned to unit {unit.UnitNumber}, {assignedBedNumber}.",
            "/AccommodationApplication/Index",
            application.Id);

        return Ok(ToApplication(application, unit.UnitNumber));
    }

    [HttpPost("applications/{id:int}/deny")]
    public async Task<IActionResult> DenyApplication(int id, AdminNotesRequest request)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;
        if (request.AdminNotes?.Length > 2000) return BadRequest(new { message = "Admin notes must not exceed 2,000 characters." });

        var application = await _db.AccommodationApplications
            .Include(item => item.User)
            .Include(item => item.Accommodation)
            .Include(item => item.AssignedUnit)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (application == null) return NotFound();
        if (application.Status != AccommodationApplicationStatus.Pending)
            return BadRequest(new { message = "Only pending applications can be denied." });

        application.Status = AccommodationApplicationStatus.Denied;
        application.AdminNotes = TrimOrNull(request.AdminNotes);
        application.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Accommodation application {ApplicationId} declined for user {UserId}.", application.Id, application.UserId);
        _notifications.CreateActivityNotification(
            application.UserId,
            "Accommodation application declined",
            string.IsNullOrWhiteSpace(application.AdminNotes)
                ? "Your accommodation application was declined. Open the application page for more details."
                : $"Your accommodation application was declined. Admin note: {application.AdminNotes}",
            "/AccommodationApplication/Index",
            application.Id);

        return Ok(ToApplication(application));
    }

    [HttpGet("tariffs")]
    public async Task<IActionResult> ListTariffs()
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;

        var tariffs = await _db.Tariffs.OrderBy(tariff => tariff.Type).ThenByDescending(tariff => tariff.EffectiveFrom).ToListAsync();
        return Ok(tariffs.Select(ToTariff));
    }

    [HttpPut("tariffs/{id:int}")]
    public async Task<IActionResult> UpdateTariff(int id, TariffUpdateRequest request)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;
        if (request.Amount < 0.01m || request.Amount > 100000m)
            return BadRequest(new { message = "Amount must be between 0.01 and 100000." });
        if (!Enum.IsDefined(request.Type)) return BadRequest(new { message = "Choose a valid tariff type." });
        if (request.Description?.Length > 200) return BadRequest(new { message = "Description must not exceed 200 characters." });

        var tariff = await _db.Tariffs.FirstOrDefaultAsync(item => item.Id == id);
        if (tariff == null) return NotFound();

        // Effective dates are intentionally not changed: this is AdminController.TariffConfigEdit.
        tariff.Type = request.Type;
        tariff.Amount = request.Amount;
        tariff.Description = TrimOrNull(request.Description);
        tariff.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(ToTariff(tariff));
    }

    [HttpPost("bills/calculate")]
    public async Task<IActionResult> CalculateBills(BillCalculationRequest request)
    {
        var rejection = await RequireAdminAsync();
        if (rejection != null) return rejection;
        if (request.Month < 1 || request.Month > 12)
            return BadRequest(new { message = "Month must be between 1 and 12." });
        if (request.Year < 2000)
            return BadRequest(new { message = "Year must be valid." });

        _tariffs.CalculateBills(request.Year, request.Month);
        var monthName = CultureInfo.GetCultureInfo("en-US").DateTimeFormat.GetMonthName(request.Month);
        return Ok(new BillCalculationResultDto(request.Year, request.Month, $"Bills for {monthName} {request.Year} have been calculated and created."));
    }

    private async Task<IActionResult?> RequireAdminAsync()
    {
        var token = Request.Headers.Authorization.ToString().Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetUserId(token, out var userId))
            return Unauthorized(new { message = "A valid bearer session is required." });

        var user = await _users.FindByIdAsync(userId);
        if (user == null) return Unauthorized(new { message = "The bearer session user no longer exists." });
        if (!await _users.IsInRoleAsync(user, "Admin"))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Administrator access is required." });

        return null;
    }

    private static bool TryValidateAccommodation(AccommodationWriteRequest request, out string error)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
        {
            error = "Name is required and must not exceed 100 characters.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.Address) || request.Address.Trim().Length > 200)
        {
            error = "Address is required and must not exceed 200 characters.";
            return false;
        }
        if (request.Description?.Length > 500)
        {
            error = "Description must not exceed 500 characters.";
            return false;
        }
        if (request.ImageUrls?.Length > 4000)
        {
            error = "Image URLs must not exceed 4,000 characters.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryValidateUnit(UnitWriteRequest request, out string error)
    {
        if (string.IsNullOrWhiteSpace(request.UnitNumber) || request.UnitNumber.Trim().Length > 10)
        {
            error = "Unit number is required and must not exceed 10 characters.";
            return false;
        }
        if (request.Capacity < 1 || request.Capacity > 20)
        {
            error = "Capacity must be between 1 and 20.";
            return false;
        }
        if (!Enum.IsDefined(request.OccupancyType))
        {
            error = "Choose a valid occupancy type.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AccommodationDto ToAccommodation(Accommodation accommodation) => new(
        accommodation.Id,
        accommodation.Name,
        accommodation.Address,
        accommodation.Description,
        accommodation.ImageUrls,
        accommodation.Units.Count,
        accommodation.Units.Count(unit => unit.IsAvailable),
        accommodation.CreatedAt,
        accommodation.UpdatedAt);

    private static UnitDto ToUnit(Unit unit) => new(
        unit.Id,
        unit.AccommodationId,
        unit.Accommodation?.Name ?? string.Empty,
        unit.UnitNumber,
        unit.Capacity,
        unit.OccupancyType.ToString(),
        unit.IsAvailable,
        unit.CreatedAt,
        unit.UpdatedAt);

    private static ApplicationDto ToApplication(AccommodationApplication application, string? assignedUnitNumber = null) => new(
        application.Id,
        application.UserId,
        $"{application.User?.FirstName} {application.User?.LastName}".Trim(),
        application.User?.Email ?? string.Empty,
        application.AccommodationId,
        application.Accommodation?.Name ?? string.Empty,
        application.Status.ToString(),
        application.AssignedUnitId,
        assignedUnitNumber ?? application.AssignedUnit?.UnitNumber,
        application.ApplicantMessage,
        application.AdminNotes,
        application.DocumentUrls,
        application.CreatedAt,
        application.ReviewedAt);

    private static TariffDto ToTariff(Tariff tariff) => new(
        tariff.Id,
        tariff.Type.ToString(),
        tariff.Amount,
        tariff.Description,
        tariff.EffectiveFrom,
        tariff.EffectiveTo,
        tariff.CreatedAt,
        tariff.UpdatedAt);

    public record AccommodationWriteRequest(string? Name, string? Address, string? Description, string? ImageUrls);
    public record AccommodationDto(int Id, string Name, string Address, string? Description, string? ImageUrls, int UnitCount, int AvailableUnitCount, DateTime CreatedAt, DateTime UpdatedAt);
    public record UnitWriteRequest(int AccommodationId, string? UnitNumber, int Capacity, UnitOccupancyType OccupancyType, bool IsAvailable);
    public record UnitDto(int Id, int AccommodationId, string AccommodationName, string UnitNumber, int Capacity, string OccupancyType, bool IsAvailable, DateTime CreatedAt, DateTime UpdatedAt);
    public record ApplicationDecisionRequest(int AssignedUnitId, string? AdminNotes);
    public record AdminNotesRequest(string? AdminNotes);
    public record ApplicationDto(int Id, string UserId, string ApplicantName, string ApplicantEmail, int AccommodationId, string AccommodationName, string Status, int? AssignedUnitId, string? AssignedUnitNumber, string? ApplicantMessage, string? AdminNotes, string? DocumentUrls, DateTime CreatedAt, DateTime? ReviewedAt);
    public record TariffUpdateRequest(TariffType Type, decimal Amount, string? Description);
    public record TariffDto(int Id, string Type, decimal Amount, string? Description, DateTime EffectiveFrom, DateTime? EffectiveTo, DateTime CreatedAt, DateTime UpdatedAt);
    public record BillCalculationRequest(int Year, int Month);
    public record BillCalculationResultDto(int Year, int Month, string Message);
    public record RevenuePointDto(int Year, int Month, string Label, decimal TotalAmount);
    public record OccupancyPointDto(int AccommodationId, string AccommodationName, int TotalUnits, int OccupiedUnits, double OccupancyRate);
    public record DashboardSummaryDto(int TotalAccommodations, int TotalUnits, int TotalBills, int TotalPayments, int PendingPayments, int TotalUsers, int TotalStudents, int TotalStaff, int TotalFinancialOfficers, int OccupiedUnits, int AvailableUnits, decimal TotalRevenue, decimal OutstandingBalance, IReadOnlyList<RevenuePointDto> Revenue, IReadOnlyList<OccupancyPointDto> Occupancy);
}
