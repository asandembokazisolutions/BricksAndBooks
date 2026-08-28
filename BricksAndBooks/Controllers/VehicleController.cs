using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Data;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;

namespace BricksAndBooks.Controllers
{
    [Authorize]
    public class VehicleController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly TariffCalculationService _tariffCalculationService;

        public VehicleController(
            IRepositoryWrapper repositoryWrapper,
            TariffCalculationService tariffCalculationService)
        {
            _repositoryWrapper = repositoryWrapper;
            _tariffCalculationService = tariffCalculationService;
        }

        // GET: Vehicle
        public IActionResult Index()
        {
            var userId = GetCurrentUserId();
            var vehicles = _repositoryWrapper.Vehicle.FindByCondition(vehicle => vehicle.UserId == userId).ToList();
            return View(vehicles);
        }

        // GET: Vehicle/Details/5
        public IActionResult Details(int id)
        {
            var vehicle = _repositoryWrapper.Vehicle.FindByCondition(item => item.Id == id).FirstOrDefault();
            if (vehicle == null)
            {
                return NotFound();
            }
            if (vehicle.UserId != GetCurrentUserId())
            {
                return Forbid();
            }

            return View(vehicle);
        }

        // GET: Vehicle/Create
        public IActionResult Create() => View();

        // POST: Vehicle/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(string registrationNumber, string make, string model, string colour, string vehicleType)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var normalizedRegistration = NormalizeRegistrationNumber(registrationNumber);
            if (string.IsNullOrWhiteSpace(normalizedRegistration))
            {
                ModelState.AddModelError(nameof(registrationNumber), "Enter a valid vehicle registration number.");
            }
            else if (normalizedRegistration.Length > 20)
            {
                ModelState.AddModelError(nameof(registrationNumber), "The vehicle registration number cannot exceed 20 letters or numbers.");
            }
            else if (RegistrationNumberExists(normalizedRegistration))
            {
                ModelState.AddModelError(nameof(registrationNumber), "This vehicle registration number is already registered on the system.");
            }

            var submittedVehicle = new Vehicle
            {
                UserId = userId,
                RegistrationNumber = normalizedRegistration,
                Make = make,
                Model = model,
                Colour = colour,
                VehicleType = vehicleType
            };
            if (!ModelState.IsValid)
            {
                return View(submittedVehicle);
            }

            var vehicle = submittedVehicle;

            _repositoryWrapper.Vehicle.Create(vehicle);
            _repositoryWrapper.Save();
            UpdateVehicleChargeMessage(userId, "Vehicle registered.");
            return RedirectToAction(nameof(Index));
        }

        // GET: Vehicle/Edit/5
        public IActionResult Edit(int id)
        {
            var vehicle = _repositoryWrapper.Vehicle.FindByCondition(item => item.Id == id).FirstOrDefault();
            if (vehicle == null)
            {
                return NotFound();
            }
            if (vehicle.UserId != GetCurrentUserId())
            {
                return Forbid();
            }

            return View(vehicle);
        }

        // POST: Vehicle/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, string registrationNumber, string make, string model, string colour, string vehicleType)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var vehicle = _repositoryWrapper.Vehicle.FindByCondition(item => item.Id == id).FirstOrDefault();
            if (vehicle == null)
            {
                return NotFound();
            }
            if (vehicle.UserId != userId)
            {
                return Forbid();
            }
            var normalizedRegistration = NormalizeRegistrationNumber(registrationNumber);
            if (string.IsNullOrWhiteSpace(normalizedRegistration))
            {
                ModelState.AddModelError(nameof(registrationNumber), "Enter a valid vehicle registration number.");
            }
            else if (normalizedRegistration.Length > 20)
            {
                ModelState.AddModelError(nameof(registrationNumber), "The vehicle registration number cannot exceed 20 letters or numbers.");
            }
            else if (RegistrationNumberExists(normalizedRegistration, vehicle.Id))
            {
                ModelState.AddModelError(nameof(registrationNumber), "This vehicle registration number is already registered on the system.");
            }

            vehicle.RegistrationNumber = normalizedRegistration;
            vehicle.Make = make;
            vehicle.Model = model;
            vehicle.Colour = colour;
            vehicle.VehicleType = vehicleType;
            if (!ModelState.IsValid)
            {
                return View(vehicle);
            }

            vehicle.UpdatedAt = DateTime.UtcNow;
            _repositoryWrapper.Vehicle.Update(vehicle);
            _repositoryWrapper.Save();

            UpdateVehicleChargeMessage(userId, "Vehicle details updated.");
            return RedirectToAction(nameof(Index));
        }

        // GET: Vehicle/Delete/5
        public IActionResult Delete(int id)
        {
            var vehicle = _repositoryWrapper.Vehicle.FindByCondition(item => item.Id == id).FirstOrDefault();
            if (vehicle == null)
            {
                return NotFound();
            }
            if (vehicle.UserId != GetCurrentUserId())
            {
                return Forbid();
            }

            return View(vehicle);
        }

        // POST: Vehicle/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var vehicle = _repositoryWrapper.Vehicle.FindByCondition(item => item.Id == id).FirstOrDefault();
            if (vehicle == null)
            {
                return NotFound();
            }
            if (vehicle.UserId != userId)
            {
                return Forbid();
            }

            _repositoryWrapper.Vehicle.Delete(vehicle);
            _repositoryWrapper.Save();
            UpdateVehicleChargeMessage(userId, "Vehicle removed.");
            return RedirectToAction(nameof(Index));
        }

        private string? GetCurrentUserId() => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        private bool RegistrationNumberExists(string normalizedRegistrationNumber, int? exceptVehicleId = null)
        {
            return _repositoryWrapper.Vehicle.FindAll()
                .AsEnumerable()
                .Any(vehicle => vehicle.Id != exceptVehicleId
                    && NormalizeRegistrationNumber(vehicle.RegistrationNumber) == normalizedRegistrationNumber);
        }

        private static string NormalizeRegistrationNumber(string? registrationNumber)
        {
            return string.Concat((registrationNumber ?? string.Empty)
                .Where(char.IsLetterOrDigit))
                .ToUpperInvariant();
        }

        private void UpdateVehicleChargeMessage(string userId, string actionMessage)
        {
            var bill = _tariffCalculationService.RefreshVehicleChargesForCurrentBill(userId);
            if (bill == null)
            {
                TempData["StatusMessage"] = $"{actionMessage} The parking charge will appear when you have an active accommodation allocation and an unpaid bill for the current month.";
                return;
            }

            var vehicleCharge = bill.ParkingAmount + bill.AdditionalVehicleAmount;
            TempData["StatusMessage"] = vehicleCharge > 0
                ? $"{actionMessage} Parking and vehicle charges of {vehicleCharge:C} have been applied to your current bill."
                : $"{actionMessage} Parking and vehicle charges were removed from your current bill.";
        }
    }
}
