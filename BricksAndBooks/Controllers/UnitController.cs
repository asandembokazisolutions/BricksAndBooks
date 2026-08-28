using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Data;
using BricksAndBooks.Models;

namespace BricksAndBooks.Controllers
{
    public class UnitController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;

        public UnitController(IRepositoryWrapper repositoryWrapper)
        {
            _repositoryWrapper = repositoryWrapper;
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
            var unit = _repositoryWrapper.Unit.FindByCondition(u => u.Id == id).FirstOrDefault();
            if (unit != null)
            {
                var accommodationId = unit.AccommodationId;
                _repositoryWrapper.Unit.Delete(unit);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index), new { accommodationId = accommodationId });
            }
            return RedirectToAction(nameof(Index));
        }
    }
}