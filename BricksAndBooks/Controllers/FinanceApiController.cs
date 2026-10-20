using System.Security.Claims;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Controllers;

/// <summary>
/// JSON finance operations for bearer-session clients. The route deliberately uses the
/// application session service rather than MVC cookie authentication, matching the existing
/// mobile API contract.
/// </summary>
[ApiController]
[Route("api/v1/finance")]
public sealed class FinanceApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ApiSessionService _sessions;
    private readonly NotificationService _notifications;
    private readonly PaymentAnomalyDetectionService _anomalies;
    private readonly AuditService _audit;

    public FinanceApiController(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        ApiSessionService sessions,
        NotificationService notifications,
        PaymentAnomalyDetectionService anomalies,
        AuditService audit)
    {
        _db = db;
        _users = users;
        _sessions = sessions;
        _notifications = notifications;
        _anomalies = anomalies;
        _audit = audit;
    }

    // Mirrors FinancialController.Index: successful, pending, and failed payment totals;
    // Student/Staff customer count; pending-bill value; and the eight most recent payments.
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;

        var payments = await _db.Payments
            .AsNoTracking()
            .OrderByDescending(payment => payment.PaymentDate)
            .ToListAsync();
        var students = await _users.GetUsersInRoleAsync("Student");
        var staff = await _users.GetUsersInRoleAsync("Staff");
        var outstandingBills = await _db.Bills
            .AsNoTracking()
            .Where(bill => bill.Status == BillStatus.Pending)
            .SumAsync(bill => (decimal?)bill.RentAmount + bill.WaterAmount + bill.ElectricityAmount + bill.ParkingAmount + bill.AdditionalVehicleAmount)
            ?? 0m;

        var successful = payments.Where(payment => payment.Status == PaymentStatus.Successful).ToList();
        var pending = payments.Where(payment => payment.Status == PaymentStatus.Pending).ToList();
        var failed = payments.Where(payment => payment.Status == PaymentStatus.Failed).ToList();
        var customerCount = students.Concat(staff).Select(user => user.Id).Distinct().Count();

        return Ok(new FinanceDashboardDto(
            successful.Sum(payment => payment.Amount),
            pending.Sum(payment => payment.Amount),
            failed.Sum(payment => payment.Amount),
            successful.Count,
            pending.Count,
            failed.Count,
            customerCount,
            outstandingBills,
            payments.Take(8).Select(ToPaymentSummary).ToList()));
    }

    // Mirrors FinancialController.Customers: only users assigned the Student or Staff role.
    [HttpGet("customers")]
    public async Task<IActionResult> Customers()
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;

        var students = await _users.GetUsersInRoleAsync("Student");
        var staff = await _users.GetUsersInRoleAsync("Staff");
        var customers = students.Concat(staff)
            .GroupBy(user => user.Id)
            .Select(group => group.First())
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .ThenBy(user => user.UserName)
            .Select(ToCustomerSummary)
            .ToList();

        return Ok(customers);
    }

    // Mirrors FinancialController.CustomerDetails: bills are located through all of the
    // customer's occupancies, since bills are attached to units rather than users.
    [HttpGet("customers/{id}")]
    public async Task<IActionResult> CustomerDetails(string id)
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Customer id is required." });

        var user = await _users.FindByIdAsync(id);
        if (user == null) return NotFound(new { message = "Customer not found." });

        var unitIds = await _db.Occupancies
            .AsNoTracking()
            .Where(occupancy => occupancy.UserId == id)
            .Select(occupancy => occupancy.UnitId)
            .Distinct()
            .ToListAsync();
        var bills = await _db.Bills
            .AsNoTracking()
            .Include(bill => bill.Unit)
            .Where(bill => unitIds.Contains(bill.UnitId))
            .OrderByDescending(bill => bill.Year)
            .ThenByDescending(bill => bill.Month)
            .ToListAsync();

        return Ok(new CustomerDetailsDto(
            user.Id,
            user.UserName,
            user.Email,
            user.FirstName,
            user.LastName,
            user.IsStudent,
            user.IsStaff,
            user.StudentNumber,
            user.EmployeeNumber,
            bills.Select(ToBill).ToList()));
    }

    // Mirrors FinancialController.Transactions. Detail-only allocation lines are kept out of
    // this list in the same way the MVC transaction list only contains transaction columns.
    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions()
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;

        var payments = await _db.Payments
            .AsNoTracking()
            .OrderByDescending(payment => payment.PaymentDate)
            .ToListAsync();
        return Ok(payments.Select(ToPaymentSummary).ToList());
    }

    [HttpGet("transactions/{id:int}")]
    public async Task<IActionResult> TransactionDetails(int id)
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;

        var payment = await FindPaymentForDetailsAsync(id);
        return payment == null
            ? NotFound(new { message = "Payment not found." })
            : Ok(ToPaymentDetail(payment));
    }

    // Mirrors PaymentController.Verify. Only a payment that is not already successful or
    // refunded can be reviewed, and all allocated bills are recalculated afterwards.
    [HttpPost("transactions/{id:int}/verify")]
    public async Task<IActionResult> VerifyPayment(int id, [FromBody] PaymentVerificationRequest? request)
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;
        if (request == null || string.IsNullOrWhiteSpace(request.Status)
            || !Enum.TryParse<PaymentStatus>(request.Status.Trim(), true, out var status)
            || !Enum.IsDefined(status))
        {
            return BadRequest(new { message = "Status must be a valid payment status." });
        }

        var payment = await _db.Payments
            .Include(item => item.Allocations)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (payment == null) return NotFound(new { message = "Payment not found." });
        if (payment.Status is PaymentStatus.Successful or PaymentStatus.Refunded)
        {
            return BadRequest(new { message = $"This payment is finalized as {payment.Status} and cannot be reviewed again." });
        }

        var previousStatus = payment.Status;
        payment.Status = status;
        payment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        foreach (var billId in payment.Allocations.Select(allocation => allocation.BillId).Distinct())
        {
            await RecalculateBillStatusAsync(billId);
        }
        await _db.SaveChangesAsync();

        var customerUserId = await GetPaymentCustomerUserIdAsync(payment);
        if (!string.IsNullOrWhiteSpace(customerUserId))
        {
            if (status == PaymentStatus.Successful)
            {
                _notifications.CreatePaymentSuccessfulNotification(customerUserId, payment.Id, payment.Amount);
            }
            else if (status == PaymentStatus.Failed)
            {
                _notifications.CreatePaymentFailedNotification(customerUserId, payment.Id, payment.Amount);
            }
            else
            {
                _notifications.CreateActivityNotification(customerUserId, "Payment reviewed", $"Your payment of {payment.Amount:C} was reviewed and marked {status}.", $"/Payment/Details/{payment.Id}", payment.Id);
            }
        }

        if (access.Roles.Contains("FinancialOfficer", StringComparer.Ordinal)
            && !access.Roles.Contains("Admin", StringComparer.Ordinal))
        {
            _notifications.CreateRoleActivityNotification("Admin", "Payment reviewed", $"Financial Officer {access.User!.UserName} marked payment {payment.TransactionReferenceId} as {status}.", $"/Payment/Details/{payment.Id}", payment.Id);
        }

        _audit.Record(
            ToActor(access),
            "Verify",
            "Payment",
            payment.Id,
            $"Changed payment status from {previousStatus} to {status} for {payment.Allocations.Count} selected billing item(s).");

        var refreshedPayment = await FindPaymentForDetailsAsync(payment.Id);
        return Ok(ToPaymentDetail(refreshedPayment!));
    }

    // Mirrors ReportingController.Revenue and ReportingController.Occupancy. Revenue includes
    // only successful payments and occupancy follows Unit.IsAvailable, as the MVC reports do.
    [HttpGet("reports/summary")]
    public async Task<IActionResult> ReportsSummary()
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;

        var revenue = await _db.Payments
            .AsNoTracking()
            .Where(payment => payment.Status == PaymentStatus.Successful)
            .GroupBy(payment => new { payment.PaymentDate.Year, payment.PaymentDate.Month })
            .Select(group => new RevenueReportDto(group.Key.Year, group.Key.Month, group.Sum(payment => payment.Amount)))
            .OrderByDescending(item => item.Year)
            .ThenByDescending(item => item.Month)
            .ToListAsync();
        var accommodations = await _db.Accommodations
            .AsNoTracking()
            .Include(accommodation => accommodation.Units)
            .OrderBy(accommodation => accommodation.Name)
            .ToListAsync();
        var occupancy = accommodations.Select(accommodation =>
        {
            var totalUnits = accommodation.Units.Count;
            var occupiedUnits = accommodation.Units.Count(unit => !unit.IsAvailable);
            var occupancyRate = totalUnits > 0 ? (double)occupiedUnits / totalUnits * 100 : 0d;
            return new OccupancyReportDto(accommodation.Name, totalUnits, occupiedUnits, occupancyRate);
        }).ToList();

        return Ok(new FinanceReportsSummaryDto(revenue, occupancy));
    }

    // Mirrors PaymentAnomalyController.Index and exposes only the anomalies returned by the
    // existing detection service; there is no synthetic anomaly state.
    [HttpGet("anomalies")]
    public async Task<IActionResult> PaymentAnomalies()
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;

        var anomalies = _anomalies.DetectAnomalies()
            .Where(anomaly => anomaly.Payment != null)
            .Select(anomaly => new PaymentAnomalyDto(
                anomaly.Payment!.Id,
                ToPaymentSummary(anomaly.Payment),
                anomaly.AnomalyType,
                anomaly.Description,
                anomaly.Severity))
            .ToList();
        return Ok(anomalies);
    }

    // The MVC Resolve action records the investigation in the audit log but does not mutate the
    // detected payment. Preserve that behavior so resolving an API anomaly cannot fabricate a
    // persistent resolution state that does not exist in the domain model.
    [HttpPost("anomalies/{id:int}/resolve")]
    public async Task<IActionResult> ResolvePaymentAnomaly(int id)
    {
        var access = await GetFinanceAccessAsync();
        if (access.Error != null) return access.Error;

        var anomaly = _anomalies.DetectAnomalies().FirstOrDefault(item => item.Payment?.Id == id);
        if (anomaly == null) return NotFound(new { message = "Payment anomaly not found." });

        _audit.Record(
            ToActor(access),
            "Investigate",
            "PaymentAnomaly",
            id,
            $"Reviewed {anomaly.AnomalyType} anomaly. {anomaly.Description}");
        return NoContent();
    }

    private async Task<FinanceAccess> GetFinanceAccessAsync()
    {
        var token = Request.Headers.Authorization.ToString()
            .Replace("Bearer ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetUserId(token, out var userId))
        {
            return new FinanceAccess(null, Array.Empty<string>(), Unauthorized());
        }

        var user = await _users.FindByIdAsync(userId);
        if (user == null) return new FinanceAccess(null, Array.Empty<string>(), Unauthorized());

        var roles = (await _users.GetRolesAsync(user)).ToArray();
        if (!roles.Contains("Admin", StringComparer.Ordinal) && !roles.Contains("FinancialOfficer", StringComparer.Ordinal))
        {
            return new FinanceAccess(user, roles, Forbid());
        }

        return new FinanceAccess(user, roles, null);
    }

    private async Task<Payment?> FindPaymentForDetailsAsync(int id) => await _db.Payments
        .AsNoTracking()
        .Include(payment => payment.Bill)
        .Include(payment => payment.Allocations)
        .ThenInclude(allocation => allocation.Bill)
        .FirstOrDefaultAsync(payment => payment.Id == id);

    private async Task RecalculateBillStatusAsync(int billId)
    {
        var bill = await _db.Bills.FirstOrDefaultAsync(item => item.Id == billId);
        if (bill == null) return;

        var payableChargeTypes = Enum.GetValues<BillChargeType>()
            .Where(chargeType => GetChargeAmount(bill, chargeType) > 0m)
            .ToList();
        var isFullyPaid = payableChargeTypes.Any();
        foreach (var chargeType in payableChargeTypes)
        {
            if (await GetSuccessfulAllocatedAmountAsync(bill, chargeType) < GetChargeAmount(bill, chargeType))
            {
                isFullyPaid = false;
                break;
            }
        }

        bill.Status = isFullyPaid ? BillStatus.Paid : BillStatus.Pending;
        bill.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<decimal> GetSuccessfulAllocatedAmountAsync(Bill bill, BillChargeType chargeType)
    {
        var allocationAmount = await _db.PaymentAllocations
            .Where(allocation => allocation.BillId == bill.Id
                && allocation.ChargeType == chargeType
                && allocation.Payment.Status == PaymentStatus.Successful)
            .SumAsync(allocation => (decimal?)allocation.Amount) ?? 0m;
        var legacyPaymentAmount = await _db.Payments
            .Where(payment => payment.BillId == bill.Id
                && !payment.Allocations.Any()
                && payment.Status == PaymentStatus.Successful)
            .SumAsync(payment => (decimal?)payment.Amount) ?? 0m;
        if (legacyPaymentAmount <= 0m) return allocationAmount;

        var remainingLegacyAmount = legacyPaymentAmount;
        foreach (var type in new[]
        {
            BillChargeType.Rent,
            BillChargeType.Water,
            BillChargeType.Electricity,
            BillChargeType.Parking,
            BillChargeType.AdditionalVehicle
        })
        {
            var amountForType = Math.Min(remainingLegacyAmount, GetChargeAmount(bill, type));
            if (type == chargeType) return allocationAmount + amountForType;
            remainingLegacyAmount = Math.Max(0m, remainingLegacyAmount - amountForType);
        }

        return allocationAmount;
    }

    private async Task<string?> GetPaymentCustomerUserIdAsync(Payment payment)
    {
        var billIds = payment.Allocations.Select(allocation => allocation.BillId).ToList();
        if (!billIds.Any() && payment.BillId.HasValue) billIds.Add(payment.BillId.Value);
        if (!billIds.Any()) return null;

        var unitId = await _db.Bills
            .Where(bill => billIds.Contains(bill.Id))
            .Select(bill => (int?)bill.UnitId)
            .FirstOrDefaultAsync();
        if (!unitId.HasValue) return null;

        return await _db.Occupancies
            .Where(occupancy => occupancy.UnitId == unitId.Value)
            .OrderByDescending(occupancy => occupancy.EndDate == null)
            .ThenByDescending(occupancy => occupancy.StartDate)
            .Select(occupancy => occupancy.UserId)
            .FirstOrDefaultAsync();
    }

    private static ClaimsPrincipal ToActor(FinanceAccess access)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, access.User!.Id) };
        claims.AddRange(access.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static CustomerSummaryDto ToCustomerSummary(ApplicationUser user)
    {
        var accountType = user.IsStudent ? "Student" : "Staff";
        var universityReference = user.IsStudent ? user.StudentNumber : user.EmployeeNumber;
        return new CustomerSummaryDto(user.Id, user.UserName, user.Email, user.FirstName, user.LastName, accountType, universityReference);
    }

    private static PaymentSummaryDto ToPaymentSummary(Payment payment) => new(
        payment.Id,
        payment.TransactionReferenceId,
        payment.Amount,
        payment.Method.ToString(),
        payment.Status.ToString(),
        payment.PaymentDate);

    private static PaymentDetailDto ToPaymentDetail(Payment payment) => new(
        ToPaymentSummary(payment),
        payment.BillId,
        payment.Allocations
            .OrderBy(allocation => allocation.Bill?.Year)
            .ThenBy(allocation => allocation.Bill?.Month)
            .ThenBy(allocation => allocation.ChargeType)
            .Select(allocation => new PaymentAllocationDto(
                allocation.Id,
                allocation.BillId,
                allocation.Bill?.Year,
                allocation.Bill?.Month,
                allocation.ChargeType.ToString(),
                allocation.Amount,
                allocation.Quantity))
            .ToList());

    private static BillDto ToBill(Bill bill) => new(
        bill.Id,
        bill.UnitId,
        bill.Unit?.UnitNumber,
        bill.Year,
        bill.Month,
        bill.RentAmount,
        bill.WaterAmount,
        bill.ElectricityAmount,
        bill.ParkingAmount,
        bill.AdditionalVehicleAmount,
        bill.TotalAmount,
        bill.Status.ToString(),
        bill.DueDate);

    private static decimal GetChargeAmount(Bill bill, BillChargeType chargeType) => chargeType switch
    {
        BillChargeType.Rent => bill.RentAmount,
        BillChargeType.Water => bill.WaterAmount,
        BillChargeType.Electricity => bill.ElectricityAmount,
        BillChargeType.Parking => bill.ParkingAmount,
        BillChargeType.AdditionalVehicle => bill.AdditionalVehicleAmount,
        _ => 0m
    };

    private sealed record FinanceAccess(ApplicationUser? User, string[] Roles, IActionResult? Error);

    public record FinanceDashboardDto(
        decimal TotalRevenue,
        decimal TotalPending,
        decimal TotalFailed,
        int SuccessfulPaymentCount,
        int PendingPaymentCount,
        int FailedPaymentCount,
        int CustomerCount,
        decimal OutstandingBills,
        IReadOnlyList<PaymentSummaryDto> RecentPayments);
    public record CustomerSummaryDto(string Id, string? UserName, string? Email, string? FirstName, string? LastName, string AccountType, string? UniversityReference);
    public record CustomerDetailsDto(string Id, string? UserName, string? Email, string? FirstName, string? LastName, bool IsStudent, bool IsStaff, string? StudentNumber, string? EmployeeNumber, IReadOnlyList<BillDto> Bills);
    public record BillDto(int Id, int UnitId, string? UnitNumber, int Year, int Month, decimal RentAmount, decimal WaterAmount, decimal ElectricityAmount, decimal ParkingAmount, decimal AdditionalVehicleAmount, decimal TotalAmount, string Status, DateTime DueDate);
    public record PaymentSummaryDto(int Id, string TransactionReferenceId, decimal Amount, string Method, string Status, DateTime PaymentDate);
    public record PaymentAllocationDto(int Id, int BillId, int? Year, int? Month, string ChargeType, decimal Amount, decimal Quantity);
    public record PaymentDetailDto(PaymentSummaryDto Payment, int? LegacyBillId, IReadOnlyList<PaymentAllocationDto> Allocations);
    public record PaymentVerificationRequest(string? Status);
    public record RevenueReportDto(int Year, int Month, decimal TotalAmount);
    public record OccupancyReportDto(string Name, int TotalUnits, int OccupiedUnits, double OccupancyRate);
    public record FinanceReportsSummaryDto(IReadOnlyList<RevenueReportDto> Revenue, IReadOnlyList<OccupancyReportDto> Occupancy);
    public record PaymentAnomalyDto(int PaymentId, PaymentSummaryDto Payment, string? AnomalyType, string? Description, string? Severity);
}
