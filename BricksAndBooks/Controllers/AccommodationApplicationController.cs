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

        public AccommodationApplicationController(AppDbContext context, UserManager<ApplicationUser> userManager, TariffCalculationService tariffCalculationService, NotificationService notificationService, IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _tariffCalculationService = tariffCalculationService;
            _notificationService = notificationService;
            _environment = environment;
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

            return View(await query.OrderByDescending(a => a.CreatedAt).ToListAsync());
        }

        [Authorize(Roles = "Student,Staff")]
        public async Task<IActionResult> Apply()
        {
            if (await HasActiveOccupancyAsync())
            {
                TempData["ApplicationError"] = "You already have accommodation. Submit a termination request before applying again.";
                return RedirectToAction(nameof(Index));
            }

            return View(await GetAccommodationAvailabilityAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student,Staff")]
        public async Task<IActionResult> Apply(int accommodationId, string? applicantMessage, List<IFormFile>? documents)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId)) return Challenge();

            if (await HasActiveOccupancyAsync(userId))
            {
                TempData["ApplicationError"] = "You already have accommodation. Submit a termination request before applying again.";
                return RedirectToAction(nameof(Index));
            }

            var hasPendingApplication = await _context.AccommodationApplications.AnyAsync(a =>
                a.UserId == userId && a.Status == AccommodationApplicationStatus.Pending);
            if (hasPendingApplication)
            {
                TempData["ApplicationError"] = "You already have a pending accommodation application.";
                return RedirectToAction(nameof(Index));
            }

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
                _context.AccommodationApplications.Add(new AccommodationApplication
                {
                    UserId = userId,
                    AccommodationId = accommodationId,
                    ApplicantMessage = applicantMessage,
                    DocumentUrls = documentUrls,
                    Status = AccommodationApplicationStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                TempData["ApplicationSuccess"] = "Your accommodation application was submitted for admin review.";
                return RedirectToAction(nameof(Index));
            }

            return View(accommodationOptions);
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

            var activeResidents = await _context.Occupancies.CountAsync(o => o.UnitId == unit.Id && (o.EndDate == null || o.EndDate > DateTime.UtcNow));
            if (activeResidents >= unit.Capacity || (unit.OccupancyType == UnitOccupancyType.Single && activeResidents > 0))
            {
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
                _notificationService.CreateRentDueNotification(application.UserId, firstBill.Id, firstBill.TotalAmount, firstBill.DueDate);
            }
            TempData["ApplicationSuccess"] = "Accommodation approved. The applicant's first rent and utility bill is now ready for payment.";
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
