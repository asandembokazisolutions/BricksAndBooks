using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Data;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;

namespace BricksAndBooks.Controllers
{
    public class AccommodationController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly IWebHostEnvironment _environment;
        private readonly AppDbContext _context;

        public AccommodationController(IRepositoryWrapper repositoryWrapper, IWebHostEnvironment environment, AppDbContext context)
        {
            _repositoryWrapper = repositoryWrapper;
            _environment = environment;
            _context = context;
        }

        // GET: Accommodation
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            var accommodations = _repositoryWrapper.Accommodation.FindAll().Include(a => a.Units).ToList();
            var accommodationIds = accommodations.Select(accommodation => accommodation.Id).ToList();
            var approvedReviews = _repositoryWrapper.Review.FindAll()
                .Where(review => accommodationIds.Contains(review.AccommodationId) && review.IsApproved)
                .Select(review => new { review.AccommodationId, review.Rating })
                .ToList();
            var reviewSummaries = approvedReviews.GroupBy(review => review.AccommodationId)
                .ToDictionary(group => group.Key, group => new { Count = group.Count(), Average = group.Average(review => (double)review.Rating) });
            ViewBag.ReviewCounts = reviewSummaries.ToDictionary(item => item.Key, item => item.Value.Count);
            ViewBag.ReviewAverages = reviewSummaries.ToDictionary(item => item.Key, item => item.Value.Average);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.MonthlyRentRate = _repositoryWrapper.Tariff.FindAll()
                .Where(tariff => tariff.Type == TariffType.Rent && tariff.EffectiveFrom <= DateTime.UtcNow
                    && (tariff.EffectiveTo == null || tariff.EffectiveTo >= DateTime.UtcNow))
                .OrderByDescending(tariff => tariff.EffectiveFrom)
                .Select(tariff => (decimal?)tariff.Amount).FirstOrDefault();
            ViewBag.HasActiveAccommodation = !string.IsNullOrWhiteSpace(userId)
                && _repositoryWrapper.Occupancy.FindAll().Any(occupancy =>
                    occupancy.UserId == userId && (occupancy.EndDate == null || occupancy.EndDate > DateTime.UtcNow));
            return View("Index", accommodations);
        }

        // GET: Accommodation/Details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var accommodation = _repositoryWrapper.Accommodation.FindAll().Include(a => a.Units).FirstOrDefault(a => a.Id == id);
            if (accommodation == null)
            {
                return NotFound();
            }

            ViewBag.ResidenceReviews = _repositoryWrapper.Review.FindAll()
                .Where(review => review.AccommodationId == id && review.IsApproved)
                .Include(review => review.User)
                .OrderByDescending(review => review.CreatedAt)
                .ToList();
            ViewBag.MonthlyRentRate = _repositoryWrapper.Tariff.FindAll()
                .Where(tariff => tariff.Type == TariffType.Rent && tariff.EffectiveFrom <= DateTime.UtcNow
                    && (tariff.EffectiveTo == null || tariff.EffectiveTo >= DateTime.UtcNow))
                .OrderByDescending(tariff => tariff.EffectiveFrom)
                .Select(tariff => (decimal?)tariff.Amount).FirstOrDefault();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.HasActiveAccommodation = !string.IsNullOrWhiteSpace(userId)
                && _repositoryWrapper.Occupancy.FindAll().Any(occupancy =>
                    occupancy.UserId == userId && (occupancy.EndDate == null || occupancy.EndDate > DateTime.UtcNow));

            return View(accommodation);
        }

        // GET: Accommodation/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Accommodation/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(string name, string address, string description, List<IFormFile>? images)
        {
            if (ModelState.IsValid)
            {
                var accommodation = new Accommodation { Name = name, Address = address, Description = description, ImageUrls = await SaveImagesAsync(images) };
                _repositoryWrapper.Accommodation.Create(accommodation);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index));
            }
            return View();
        }

        // GET: Accommodation/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == id).FirstOrDefault();
            if (accommodation == null)
            {
                return NotFound();
            }

            return View(accommodation);
        }

        // POST: Accommodation/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, string name, string address, string description, List<IFormFile>? images)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == id).FirstOrDefault();
            if (accommodation == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                accommodation.Name = name;
                accommodation.Address = address;
                accommodation.Description = description;
                var newImages = await SaveImagesAsync(images);
                if (!string.IsNullOrWhiteSpace(newImages))
                {
                    accommodation.ImageUrls = string.Join('|', new[] { accommodation.ImageUrls, newImages }.Where(value => !string.IsNullOrWhiteSpace(value)));
                }
                accommodation.UpdatedAt = System.DateTime.UtcNow;

                _repositoryWrapper.Accommodation.Update(accommodation);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index));
            }
            return View(accommodation);
        }

        // GET: Accommodation/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == id).FirstOrDefault();
            if (accommodation == null)
            {
                return NotFound();
            }

            return View(accommodation);
        }

        // POST: Accommodation/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var accommodation = await _context.Accommodations.FirstOrDefaultAsync(a => a.Id == id);
            if (accommodation == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var unitIds = await _context.Units
                .Where(unit => unit.AccommodationId == id)
                .Select(unit => unit.Id)
                .ToListAsync();

            // Applications and maintenance records use restrictive relationships. Remove
            // their dependent records explicitly instead of relying on database cascades.
            var applications = await _context.AccommodationApplications
                .Where(application => application.AccommodationId == id)
                .ToListAsync();
            var occupancies = await _context.Occupancies
                .Where(occupancy => unitIds.Contains(occupancy.UnitId))
                .ToListAsync();
            var maintenanceRequests = await _context.MaintenanceRequests
                .Where(request => unitIds.Contains(request.UnitId))
                .ToListAsync();
            var bills = await _context.Bills
                .Where(bill => unitIds.Contains(bill.UnitId))
                .ToListAsync();
            var billIds = bills.Select(bill => bill.Id).ToHashSet();
            var allocations = await _context.PaymentAllocations
                .Where(allocation => billIds.Contains(allocation.BillId))
                .ToListAsync();

            // Keep payment history, but remove references to bills being deleted.
            var paymentIds = allocations.Select(allocation => allocation.PaymentId).ToHashSet();
            var payments = await _context.Payments
                .Where(payment => paymentIds.Contains(payment.Id)
                    || (payment.BillId.HasValue && billIds.Contains(payment.BillId.Value)))
                .ToListAsync();
            foreach (var payment in payments)
            {
                if (payment.BillId.HasValue && billIds.Contains(payment.BillId.Value))
                {
                    payment.BillId = null;
                }
            }

            _context.PaymentAllocations.RemoveRange(allocations);
            _context.Bills.RemoveRange(bills);
            _context.Occupancies.RemoveRange(occupancies);
            _context.MaintenanceRequests.RemoveRange(maintenanceRequests);
            _context.AccommodationApplications.RemoveRange(applications);
            _context.Units.RemoveRange(_context.Units.Where(unit => unit.AccommodationId == id));
            _context.Accommodations.Remove(accommodation);
            await _context.SaveChangesAsync();

            TempData["StatusMessage"] = $"{accommodation.Name} and its related residence records were deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<string?> SaveImagesAsync(IEnumerable<IFormFile>? images)
        {
            if (images == null)
            {
                return null;
            }

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "accommodations");
            Directory.CreateDirectory(uploadRoot);
            var paths = new List<string>();
            foreach (var image in images.Where(file => file.Length > 0).Take(10))
            {
                var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
                if (!allowed.Contains(extension) || image.Length > 5 * 1024 * 1024)
                {
                    continue;
                }

                var fileName = $"{Guid.NewGuid():N}{extension}";
                var physicalPath = Path.Combine(uploadRoot, fileName);
                await using var stream = System.IO.File.Create(physicalPath);
                await image.CopyToAsync(stream);
                paths.Add($"/uploads/accommodations/{fileName}");
            }
            return paths.Count == 0 ? null : string.Join('|', paths);
        }
    }
}
