using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using BricksAndBooks.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Controllers
{
    [Authorize]
    public class AccommodationApplicationController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly TariffCalculationService _tariffCalculationService;
        private readonly NotificationService _notificationService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<AccommodationApplicationController> _logger;
        private static readonly SemaphoreSlim AllocationGate = new(1, 1);

        public AccommodationApplicationController(AppDbContext context, UserManager<ApplicationUser> userManager, TariffCalculationService tariffCalculationService, NotificationService notificationService, IWebHostEnvironment environment, ILogger<AccommodationApplicationController> logger)
        {
            _context = context;
            _userManager = userManager;
            _tariffCalculationService = tariffCalculationService;
            _notificationService = notificationService;
            _environment = environment;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var query = _context.AccommodationApplications
                .Include(a => a.User)
                .Include(a => a.Accommodation)
                .ThenInclude(a => a!.Units)
                .Include(a => a.AssignedUnit)
                .AsQueryable();

            if (!User.IsInRole("Admin"))
            {
                var userId = _userManager.GetUserId(User);
                query = query.Where(a => a.UserId == userId);
            }

            var applications = await query
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return View("Index", applications);
        }

        [Authorize(Roles = "Student,Staff")]
        public async Task<IActionResult> Apply()
        {
            var accommodationOptions = await GetAccommodationAvailabilityAsync();
            return View("Apply", accommodationOptions);
        }

        [Authorize(Roles = "Student,Staff")]
        public async Task<IActionResult> DownloadProof()
        {
            var userId = _userManager.GetUserId(User);
            var occupancy = await _context.Occupancies
                .Include(item => item.User)
                .Include(item => item.Unit)
                .ThenInclude(item => item!.Accommodation)
                .Where(item => item.UserId == userId && (item.EndDate == null || item.EndDate > DateTime.UtcNow))
                .OrderByDescending(item => item.StartDate)
                .FirstOrDefaultAsync();
            if (occupancy?.Unit?.Accommodation == null || occupancy.User == null)
            {
                TempData["ApplicationError"] = "A proof of accommodation is available after your accommodation has been approved.";
                return RedirectToAction(nameof(Index));
            }

            var user = occupancy.User;
            var accommodation = occupancy.Unit.Accommodation;
            var ufsLogo = ReadImageAsDataUri("ufs-logo.png", "image/png");
            var bnbLogo = ReadImageAsDataUri("bnb-logo.png", "image/png");
            var reference = $"B&B-{occupancy.Id:D6}";
            var fullName = System.Net.WebUtility.HtmlEncode($"{user.FirstName} {user.LastName}");
            var html = $@"<!doctype html>
<html><head><meta charset='utf-8'><title>Proof of Accommodation - {System.Net.WebUtility.HtmlEncode(user.FirstName)}</title>
<style>body{{font-family:Arial,sans-serif;color:#183b56;margin:0;background:#f3f6f8}}.page{{max-width:850px;margin:35px auto;background:#fff;padding:48px;box-shadow:0 5px 24px #183b5620}}.logos{{display:flex;align-items:center;justify-content:space-between;border-bottom:5px solid #f2b134;padding-bottom:22px}}.logos img{{max-height:72px;max-width:230px}}h1{{font-size:30px;margin:42px 0 8px}}.sub{{color:#637789}}.notice{{background:#edf5fa;padding:18px 22px;border-left:5px solid #183b56;margin:28px 0}}.grid{{display:grid;grid-template-columns:1fr 1fr;border:1px solid #d7e1e8;margin-top:25px}}.cell{{padding:17px;border-bottom:1px solid #d7e1e8}}.cell:nth-child(odd){{border-right:1px solid #d7e1e8}}.label{{display:block;font-size:11px;text-transform:uppercase;color:#637789;font-weight:bold;margin-bottom:6px}}.value{{font-size:17px;font-weight:bold}}footer{{margin-top:50px;padding-top:18px;border-top:1px solid #d7e1e8;color:#637789;font-size:12px}}</style></head>
<body><main class='page'><div class='logos'><img src='{ufsLogo}' alt='University logo'><img src='{bnbLogo}' alt='Bricks and Books logo'></div>
<h1>Proof of Accommodation</h1><div class='sub'>Official residence allocation confirmation · Reference {reference}</div>
<div class='notice'>This document confirms that the student or staff member named below has an active accommodation allocation in the Bricks &amp; Books residence portal.</div>
<div class='grid'><div class='cell'><span class='label'>Full name</span><span class='value'>{fullName}</span></div><div class='cell'><span class='label'>Email</span><span class='value'>{System.Net.WebUtility.HtmlEncode(user.Email ?? string.Empty)}</span></div><div class='cell'><span class='label'>Residence</span><span class='value'>{System.Net.WebUtility.HtmlEncode(accommodation.Name)}</span></div><div class='cell'><span class='label'>Address</span><span class='value'>{System.Net.WebUtility.HtmlEncode(accommodation.Address)}</span></div><div class='cell'><span class='label'>Unit / room</span><span class='value'>{System.Net.WebUtility.HtmlEncode(occupancy.Unit.UnitNumber)}</span></div><div class='cell'><span class='label'>Bed allocation</span><span class='value'>{System.Net.WebUtility.HtmlEncode(occupancy.BedNumber)}</span></div><div class='cell'><span class='label'>Accommodation type</span><span class='value'>{occupancy.Unit.OccupancyType}</span></div><div class='cell'><span class='label'>Allocation start</span><span class='value'>{occupancy.StartDate.ToLocalTime():dd MMMM yyyy}</span></div></div>
<footer>Issued by Bricks &amp; Books Residence Portal on {DateTime.UtcNow.ToLocalTime():dd MMMM yyyy HH:mm}. Verify this document using reference {reference}.</footer></main></body></html>";
            return File(System.Text.Encoding.UTF8.GetBytes(html), "text/html", $"Proof-of-Accommodation-{reference}.html");
        }

        private string ReadImageAsDataUri(string fileName, string contentType)
        {
            var path = Path.Combine(_environment.WebRootPath, "images", fileName);
            return System.IO.File.Exists(path) ? $"data:{contentType};base64,{Convert.ToBase64String(System.IO.File.ReadAllBytes(path))}" : string.Empty;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student,Staff")]
        public async Task<IActionResult> Apply(int accommodationId, string? applicantMessage, List<IFormFile>? documents)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId)) return Challenge();

            var accommodationOptions = await GetAccommodationAvailabilityAsync();
            var selectedAccommodation = accommodationOptions.FirstOrDefault(item => item.AccommodationId == accommodationId);
            if (selectedAccommodation == null)
            {
                ModelState.AddModelError(nameof(accommodationId), "Select a valid accommodation.");
            }
            else if (!selectedAccommodation.CanApply)
            {
                ModelState.AddModelError(nameof(accommodationId), "This residence has no available units or spaces at the moment. Please choose another residence.");
            }

            ValidateApplicationDocuments(documents);
            if (ModelState.IsValid)
            {
                var documentUrls = await SaveApplicationDocumentsAsync(documents);
                var application = new AccommodationApplication
                {
                    UserId = userId,
                    AccommodationId = accommodationId,
                    ApplicantMessage = applicantMessage,
                    DocumentUrls = documentUrls,
                    Status = AccommodationApplicationStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };
                _context.AccommodationApplications.Add(application);
                await _context.SaveChangesAsync();

                _notificationService.CreateActivityNotification(
                    userId,
                    "Accommodation application submitted",
                    "Your accommodation application was submitted and is awaiting review.",
                    $"/AccommodationApplication/Index",
                    application.Id);
                _notificationService.CreateRoleActivityNotification(
                    "Admin",
                    "New accommodation application",
                    "A new accommodation application is waiting for review.",
                    $"/AccommodationApplication/Index",
                    application.Id);

                TempData["ApplicationSuccess"] = "Your accommodation application was submitted for admin review.";
                return RedirectToAction(nameof(Index));
            }

            return View("Apply", accommodationOptions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Approve(int id, int assignedUnitId, string? adminNotes)
        {
            var application = await _context.AccommodationApplications
                .Include(a => a.Accommodation)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (application == null) return NotFound();
            if (application.Status != AccommodationApplicationStatus.Pending)
            {
                TempData["ApplicationError"] = "Only pending applications can be approved.";
                return RedirectToAction(nameof(Index));
            }

            if (await HasActiveOccupancyAsync(application.UserId))
            {
                TempData["ApplicationError"] = "This applicant already has an active accommodation.";
                return RedirectToAction(nameof(Index));
            }

            var unit = await _context.Units.FirstOrDefaultAsync(u => u.Id == assignedUnitId && u.AccommodationId == application.AccommodationId);
            if (unit == null)
            {
                TempData["ApplicationError"] = "Select a unit belonging to the requested accommodation.";
                return RedirectToAction(nameof(Index));
            }

            var assignedBedNumber = string.Empty;
            await AllocationGate.WaitAsync();
            try
            {
                // The in-memory EF compatibility cache has no row-lock primitive. This
                // process-level gate closes the check-then-write race for this app instance.
                // Firestore deployments with multiple instances should use a transactional
                // Firestore allocation or a database-level uniqueness constraint.
                var activeResidents = await _context.Occupancies.CountAsync(o => o.UnitId == unit.Id && (o.EndDate == null || o.EndDate > DateTime.UtcNow));
                if (activeResidents >= unit.Capacity || (unit.OccupancyType == UnitOccupancyType.Single && activeResidents > 0))
                {
                    _logger.LogWarning("Accommodation approval rejected because unit {UnitId} is full. Application {ApplicationId}.", unit.Id, application.Id);
                    TempData["ApplicationError"] = "The selected unit is no longer available.";
                    return RedirectToAction(nameof(Index));
                }

                var occupiedBeds = await _context.Occupancies
                    .Where(o => o.UnitId == unit.Id && (o.EndDate == null || o.EndDate > DateTime.UtcNow))
                    .Select(o => o.BedNumber)
                    .ToListAsync();
                var bedNumber = Enumerable.Range(1, unit.Capacity)
                    .Select(number => $"Bed {number}")
                    .FirstOrDefault(candidate => !occupiedBeds.Contains(candidate)) ?? $"Bed {activeResidents + 1}";
                assignedBedNumber = bedNumber;

                _context.Occupancies.Add(new Occupancy
                {
                    UserId = application.UserId,
                    UnitId = unit.Id,
                    BedNumber = bedNumber,
                    StartDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                application.AssignedUnitId = unit.Id;
                application.Status = AccommodationApplicationStatus.Approved;
                application.AdminNotes = adminNotes;
                application.ReviewedAt = DateTime.UtcNow;
                unit.IsAvailable = activeResidents + 1 < unit.Capacity;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Accommodation application {ApplicationId} approved for unit {UnitId}, bed {BedNumber}.", application.Id, unit.Id, bedNumber);
            }
            finally
            {
                AllocationGate.Release();
            }

            // Approval creates the active occupancy first. The existing tariff service
            // then issues the first pending bill for this resident, covering rent,
            // water, electricity, and parking/additional-vehicle charges when vehicles
            // are registered for the applicant.
            var approvalDate = DateTime.UtcNow;
            _tariffCalculationService.CalculateBills(approvalDate.Year, approvalDate.Month);
            var firstBill = await _context.Bills
                .Where(b => b.UnitId == unit.Id && b.Year == approvalDate.Year && b.Month == approvalDate.Month)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();
            if (firstBill != null)
            {
                // Approval must never make an unpaid bill appear settled. This is important
                // when an old bill for the same unit was restored from Firestore with a stale
                // Paid status. Only successful payments are allowed to settle a bill; pending
                // submissions still require verification by finance.
                var successfulAllocatedAmount = await _context.PaymentAllocations
                    .Where(allocation => allocation.BillId == firstBill.Id
                        && allocation.Payment.Status == PaymentStatus.Successful)
                    .SumAsync(allocation => (decimal?)allocation.Amount) ?? 0m;
                var successfulLegacyAmount = await _context.Payments
                    .Where(payment => payment.BillId == firstBill.Id
                        && !payment.Allocations.Any()
                        && payment.Status == PaymentStatus.Successful)
                    .SumAsync(payment => (decimal?)payment.Amount) ?? 0m;
                var successfulAmount = successfulAllocatedAmount + successfulLegacyAmount;
                firstBill.Status = successfulAmount >= firstBill.TotalAmount
                    && firstBill.TotalAmount > 0m
                    ? BillStatus.Paid
                    : BillStatus.Pending;
                firstBill.UpdatedAt = approvalDate;
                await _context.SaveChangesAsync();
                // The approved resident can review and pay this bill from My Bills; approval
                // itself does not launch or force a payment step.
            }
            _notificationService.CreateActivityNotification(
                application.UserId,
                "Accommodation application approved",
                $"Your application for {application.Accommodation?.Name ?? "accommodation"} was approved. You were assigned to unit {unit.UnitNumber}, {assignedBedNumber}.",
                "/AccommodationApplication/Index",
                application.Id);
            TempData["ApplicationSuccess"] = "Accommodation approved. Your allocation is confirmed; billing is available separately under My Bills when you are ready.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Deny(int id, string? adminNotes)
        {
            var application = await _context.AccommodationApplications.FindAsync(id);
            if (application == null) return NotFound();
            if (application.Status != AccommodationApplicationStatus.Pending)
            {
                TempData["ApplicationError"] = "Only pending applications can be denied.";
                return RedirectToAction(nameof(Index));
            }

            application.Status = AccommodationApplicationStatus.Denied;
            application.AdminNotes = adminNotes;
            application.ReviewedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Accommodation application {ApplicationId} declined for user {UserId}.", application.Id, application.UserId);
            _notificationService.CreateActivityNotification(
                application.UserId,
                "Accommodation application declined",
                string.IsNullOrWhiteSpace(adminNotes)
                    ? "Your accommodation application was declined. Open the application page for more details."
                    : $"Your accommodation application was declined. Admin note: {adminNotes}",
                "/AccommodationApplication/Index",
                application.Id);
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student,Staff")]
        public async Task<IActionResult> RequestTermination(string? message)
        {
            var userId = _userManager.GetUserId(User);
            var application = await _context.AccommodationApplications
                .Where(a => a.UserId == userId && a.Status == AccommodationApplicationStatus.Approved)
                .OrderByDescending(a => a.ReviewedAt)
                .FirstOrDefaultAsync();
            if (application == null)
            {
                TempData["ApplicationError"] = "You do not have an active approved accommodation.";
                return RedirectToAction(nameof(Index));
            }

            application.Status = AccommodationApplicationStatus.TerminationRequested;
            application.TerminationRequestedAt = DateTime.UtcNow;
            application.ApplicantMessage = message;
            await _context.SaveChangesAsync();
            TempData["ApplicationSuccess"] = "Your termination request was submitted to the administrator.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveTermination(int id, string? adminNotes)
        {
            var application = await _context.AccommodationApplications
                .Include(a => a.AssignedUnit)
                .FirstOrDefaultAsync(a => a.Id == id);
            if (application == null) return NotFound();
            if (application.Status != AccommodationApplicationStatus.TerminationRequested)
            {
                TempData["ApplicationError"] = "Only termination requests awaiting review can be approved.";
                return RedirectToAction(nameof(Index));
            }

            var occupancy = await _context.Occupancies
                .Where(o => o.UserId == application.UserId && o.UnitId == application.AssignedUnitId && (o.EndDate == null || o.EndDate > DateTime.UtcNow))
                .OrderByDescending(o => o.StartDate)
                .FirstOrDefaultAsync();
            if (occupancy != null)
            {
                occupancy.EndDate = DateTime.UtcNow;
                occupancy.UpdatedAt = DateTime.UtcNow;
            }

            if (application.AssignedUnit != null)
            {
                var remaining = await _context.Occupancies.CountAsync(o => o.UnitId == application.AssignedUnit.Id && (o.EndDate == null || o.EndDate > DateTime.UtcNow));
                application.AssignedUnit.IsAvailable = remaining < application.AssignedUnit.Capacity;
            }

            application.Status = AccommodationApplicationStatus.Terminated;
            application.AdminNotes = adminNotes;
            application.TerminatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Terminate(int id, string? adminNotes)
        {
            if (string.IsNullOrWhiteSpace(adminNotes))
            {
                TempData["ApplicationError"] = "A reason is required when terminating an accommodation contract.";
                return RedirectToAction(nameof(Index));
            }

            var application = await _context.AccommodationApplications
                .Include(item => item.Accommodation)
                .Include(item => item.AssignedUnit)
                .FirstOrDefaultAsync(item => item.Id == id);
            if (application == null) return NotFound();

            if (application.Status != AccommodationApplicationStatus.Approved)
            {
                TempData["ApplicationError"] = "Only an approved active accommodation can be terminated directly by an administrator.";
                return RedirectToAction(nameof(Index));
            }

            var now = DateTime.UtcNow;
            var occupancy = await _context.Occupancies
                .Where(item => item.UserId == application.UserId
                    && item.UnitId == application.AssignedUnitId
                    && (item.EndDate == null || item.EndDate > now))
                .OrderByDescending(item => item.StartDate)
                .FirstOrDefaultAsync();

            if (occupancy != null)
            {
                occupancy.EndDate = now;
                occupancy.UpdatedAt = now;
            }

            if (application.AssignedUnit != null)
            {
                var remainingResidents = await _context.Occupancies
                    .CountAsync(item => item.UnitId == application.AssignedUnit.Id
                        && (item.EndDate == null || item.EndDate > now));
                application.AssignedUnit.IsAvailable = remainingResidents < application.AssignedUnit.Capacity;
            }

            application.Status = AccommodationApplicationStatus.Terminated;
            application.AdminNotes = adminNotes.Trim();
            application.ReviewedAt = now;
            application.TerminatedAt = now;
            await _context.SaveChangesAsync();

            _logger.LogWarning(
                "Administrator terminated accommodation application {ApplicationId} for user {UserId}. Reason: {Reason}",
                application.Id,
                application.UserId,
                application.AdminNotes);
            _notificationService.CreateActivityNotification(
                application.UserId,
                "Accommodation contract terminated",
                $"Your accommodation contract for {application.Accommodation?.Name ?? "the residence"} was terminated by an administrator. Reason: {application.AdminNotes}",
                "/AccommodationApplication/Index",
                application.Id);

            TempData["ApplicationSuccess"] = "The accommodation contract was terminated and the student was notified.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DenyTermination(int id, string? adminNotes)
        {
            var application = await _context.AccommodationApplications.FindAsync(id);
            if (application == null) return NotFound();
            if (application.Status == AccommodationApplicationStatus.TerminationRequested)
            {
                application.Status = AccommodationApplicationStatus.Approved;
                application.AdminNotes = adminNotes;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<List<AccommodationAvailabilityViewModel>> GetAccommodationAvailabilityAsync()
        {
            var now = DateTime.UtcNow;
            var accommodations = await _context.Accommodations
                .AsNoTracking()
                .OrderBy(accommodation => accommodation.Name)
                .ToListAsync();
            var units = await _context.Units
                .AsNoTracking()
                .OrderBy(unit => unit.UnitNumber)
                .ToListAsync();
            var activeOccupancyCounts = await _context.Occupancies
                .Where(occupancy => occupancy.EndDate == null || occupancy.EndDate >= now)
                .GroupBy(occupancy => occupancy.UnitId)
                .Select(group => new { UnitId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.UnitId, item => item.Count);

            return accommodations.Select(accommodation =>
            {
                var accommodationUnits = units.Where(unit => unit.AccommodationId == accommodation.Id).ToList();
                var availableUnits = accommodationUnits.Count(unit =>
                    unit.Capacity - activeOccupancyCounts.GetValueOrDefault(unit.Id, 0) > 0);
                var availableSpaces = accommodationUnits.Sum(unit =>
                    Math.Max(0, unit.Capacity - activeOccupancyCounts.GetValueOrDefault(unit.Id, 0)));

                return new AccommodationAvailabilityViewModel
                {
                    AccommodationId = accommodation.Id,
                    Name = accommodation.Name,
                    Address = accommodation.Address,
                    Description = accommodation.Description,
                    TotalUnits = accommodationUnits.Count,
                    AvailableUnits = availableUnits,
                    AvailableSpaces = availableSpaces
                };
            }).ToList();
        }

        private Task<bool> HasActiveOccupancyAsync(string? userId = null)
        {
            userId ??= _userManager.GetUserId(User);
            return _context.Occupancies.AnyAsync(o => o.UserId == userId && (o.EndDate == null || o.EndDate > DateTime.UtcNow));
        }

        private void ValidateApplicationDocuments(IEnumerable<IFormFile>? documents)
        {
            var files = documents?.Where(file => file.Length > 0).ToList() ?? new List<IFormFile>();
            var allowed = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
            if (files.Count > 5) ModelState.AddModelError("documents", "Upload a maximum of five documents.");
            foreach (var file in files)
            {
                if (file.Length > 5 * 1024 * 1024 || !allowed.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
                    ModelState.AddModelError("documents", "Documents must be PDF, JPG, JPEG, or PNG files under 5 MB each.");
            }
        }

        private async Task<string?> SaveApplicationDocumentsAsync(IEnumerable<IFormFile>? documents)
        {
            var files = documents?.Where(file => file.Length > 0).ToList() ?? new List<IFormFile>();
            if (!files.Any()) return null;
            var folder = Path.Combine(_environment.WebRootPath, "uploads", "applications");
            Directory.CreateDirectory(folder);
            var urls = new List<string>();
            foreach (var file in files)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                var name = $"{Guid.NewGuid():N}{extension}";
                await using var stream = System.IO.File.Create(Path.Combine(folder, name));
                await file.CopyToAsync(stream);
                urls.Add($"/uploads/applications/{name}");
            }
            return string.Join(",", urls);
        }
    }
}
