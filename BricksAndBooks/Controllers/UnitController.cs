using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Data;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;

namespace BricksAndBooks.Controllers
{
    public class UnitController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly AppDbContext _context;

        public UnitController(IRepositoryWrapper repositoryWrapper, AppDbContext context)
        {
            _repositoryWrapper = repositoryWrapper;
            _context = context;
        }

        // GET: Unit
        [Authorize] // Allow any authenticated user to view units
        public async Task<IActionResult> Index(int? accommodationId)
        {
            if (!accommodationId.HasValue || accommodationId.Value <= 0)
            {
                TempData["UnitNavigationMessage"] = "Choose an accommodation first, then open its Units page.";
                return RedirectToAction("Index", "Accommodation");
            }

            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == accommodationId.Value).FirstOrDefault();
            if (accommodation == null)
            {
                return NotFound();
            }

            var units = _repositoryWrapper.Unit.FindByCondition(u => u.AccommodationId == accommodationId.Value).ToList();
            ViewData["AccommodationId"] = accommodationId.Value;
            ViewData["AccommodationName"] = accommodation.Name;
            return View(units);
        }

        // GET: Unit/Details/5
        [Authorize] // Allow any authenticated user to view unit details
        public async Task<IActionResult> Details(int id)
        {
            var unit = _repositoryWrapper.Unit.FindByCondition(u => u.Id == id).FirstOrDefault();
            if (unit == null)
            {
                return NotFound();
            }

            return View(unit);
        }

        // GET: Unit/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create(int accommodationId)
        {
            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == accommodationId).FirstOrDefault();
            if (accommodation == null)
            {
                return NotFound();
            }

            ViewData["AccommodationId"] = accommodationId;
            ViewData["AccommodationName"] = accommodation.Name;
            return View();
        }

        // POST: Unit/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(int accommodationId, string unitNumber, int capacity, UnitOccupancyType occupancyType)
        {
            if (ModelState.IsValid)
            {
                var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == accommodationId).FirstOrDefault();
                if (accommodation == null)
                {
                    return NotFound();
                }

                var unit = new Unit {
                    UnitNumber = unitNumber,
                    AccommodationId = accommodationId,
                    Capacity = capacity,
                    OccupancyType = occupancyType,
                    IsAvailable = true
                };
                _repositoryWrapper.Unit.Create(unit);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index), new { accommodationId = accommodationId });
            }
            return View();
        }

        // GET: Unit/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var unit = _repositoryWrapper.Unit.FindByCondition(u => u.Id == id).FirstOrDefault();
            if (unit == null)
            {
                return NotFound();
            }

            ViewData["AccommodationId"] = unit.AccommodationId;
            ViewData["AccommodationName"] = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == unit.AccommodationId).FirstOrDefault()?.Name;
            return View(unit);
        }

        // POST: Unit/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, string unitNumber, int capacity, UnitOccupancyType occupancyType, bool isAvailable)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var unit = _repositoryWrapper.Unit.FindByCondition(u => u.Id == id).FirstOrDefault();
            if (unit == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                unit.UnitNumber = unitNumber;
                unit.Capacity = capacity;
                unit.OccupancyType = occupancyType;
                unit.IsAvailable = isAvailable;
                unit.UpdatedAt = System.DateTime.UtcNow;

                _repositoryWrapper.Unit.Update(unit);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index), new { accommodationId = unit.AccommodationId });
            }
            return View(unit);
        }

        // GET: Unit/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var unit = _repositoryWrapper.Unit.FindByCondition(u => u.Id == id).FirstOrDefault();
            if (unit == null)
            {
                return NotFound();
            }

            ViewData["AccommodationId"] = unit.AccommodationId;
            ViewData["AccommodationName"] = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == unit.AccommodationId).FirstOrDefault()?.Name;
            return View(unit);
        }

        // POST: Unit/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var unit = await _context.Units.FirstOrDefaultAsync(u => u.Id == id);
            if (unit == null)
            {
                return RedirectToAction(nameof(Index));
            }

            var hasActiveResidents = await _context.Occupancies.AnyAsync(occupancy =>
                occupancy.UnitId == id && (occupancy.EndDate == null || occupancy.EndDate > DateTime.UtcNow));
            if (hasActiveResidents)
            {
                TempData["UnitNavigationMessage"] = "This unit cannot be deleted while it has active residents.";
                return RedirectToAction(nameof(Index), new { accommodationId = unit.AccommodationId });
            }

            // Preserve application history while removing the unit relationship.
            var assignedApplications = await _context.AccommodationApplications
                .Where(application => application.AssignedUnitId == id)
                .ToListAsync();
            foreach (var application in assignedApplications)
            {
                application.AssignedUnitId = null;
            }

            // Remove payment allocations before their bills; the relationship is restrictive.
            var bills = await _context.Bills.Where(bill => bill.UnitId == id).ToListAsync();
            var maintenanceRequests = await _context.MaintenanceRequests
                .Where(request => request.UnitId == id)
                .ToListAsync();
            var billIds = bills.Select(bill => bill.Id).ToHashSet();
            var allocations = await _context.PaymentAllocations
                .Where(allocation => billIds.Contains(allocation.BillId))
                .ToListAsync();
            var paymentIds = allocations.Select(allocation => allocation.PaymentId).ToHashSet();
            var payments = await _context.Payments
                .Where(payment => paymentIds.Contains(payment.Id))
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
            _context.MaintenanceRequests.RemoveRange(maintenanceRequests);
            _context.Occupancies.RemoveRange(_context.Occupancies.Where(occupancy => occupancy.UnitId == id));
            _context.Units.Remove(unit);
            await _context.SaveChangesAsync();
            TempData["UnitNavigationMessage"] = "The unit and its related billing records were deleted successfully.";
            return RedirectToAction(nameof(Index), new { accommodationId = unit.AccommodationId });
        }
    }
}
