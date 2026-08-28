using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Data;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;

namespace BricksAndBooks.Controllers
{
    [Authorize(Roles = "Admin,FinancialOfficer")]
    public class SearchController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager;

        public SearchController(IRepositoryWrapper repositoryWrapper, UserManager<ApplicationUser> userManager)
        {
            _repositoryWrapper = repositoryWrapper;
            _userManager = userManager;
        }

        // GET: Search
        public IActionResult Index()
        {
            return View(new SearchViewModel());
        }

        // POST: Search
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(SearchViewModel model)
        {
            model ??= new SearchViewModel();
            model.EnsureResultCollections();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            model.Users.Clear();
            model.Bills.Clear();
            model.Payments.Clear();
            model.Vehicles.Clear();
            model.Accommodations.Clear();
            model.Units.Clear();

            if (string.IsNullOrWhiteSpace(model.SearchTerm))
            {
                return View(model);
            }

            var searchTerm = model.SearchTerm.Trim();

            // Some .NET string overloads and numeric ToString calls cannot be translated by EF Core.
            // Load only the enabled search categories first, then apply the case-insensitive matching in memory.
            if (model.SearchIncludesUsers || model.SearchIncludesAll)
            {
                var allUsers = await _userManager.Users.AsNoTracking().ToListAsync();
                model.Users = allUsers.Where(user =>
                    ContainsIgnoreCase(user.FirstName, searchTerm) ||
                    ContainsIgnoreCase(user.LastName, searchTerm) ||
                    ContainsIgnoreCase(user.UserName, searchTerm) ||
                    ContainsIgnoreCase(user.Email, searchTerm) ||
                    ContainsIgnoreCase(user.PhoneNumber, searchTerm) ||
                    ContainsIgnoreCase(user.StudentNumber, searchTerm) ||
                    ContainsIgnoreCase(user.EmployeeNumber, searchTerm))
                    .ToList();
            }

            if (model.SearchIncludesBills || model.SearchIncludesAll)
            {
                var allBills = await _repositoryWrapper.Bill.FindAll()
                    .Include(bill => bill.Unit)
                    .ThenInclude(unit => unit.Accommodation)
                    .ToListAsync();

                model.Bills = allBills.Where(bill =>
                    ContainsIgnoreCase(bill.Id.ToString(), searchTerm) ||
                    ContainsIgnoreCase(bill.Unit?.UnitNumber, searchTerm) ||
                    ContainsIgnoreCase(bill.Unit?.Accommodation?.Name, searchTerm))
                    .ToList();
            }

            if (model.SearchIncludesPayments || model.SearchIncludesAll)
            {
                var allPayments = await _repositoryWrapper.Payment.FindAll()
                    .Include(payment => payment.Bill)
                    .ToListAsync();

                model.Payments = allPayments.Where(payment =>
                    ContainsIgnoreCase(payment.Id.ToString(), searchTerm) ||
                    ContainsIgnoreCase(payment.TransactionReferenceId, searchTerm) ||
                    ContainsIgnoreCase(payment.Bill?.Id.ToString(), searchTerm))
                    .ToList();
            }

            if (model.SearchIncludesVehicles || model.SearchIncludesAll)
            {
                var allVehicles = await _repositoryWrapper.Vehicle.FindAll().ToListAsync();
                model.Vehicles = allVehicles.Where(vehicle =>
                    ContainsIgnoreCase(vehicle.RegistrationNumber, searchTerm) ||
                    ContainsIgnoreCase(vehicle.Make, searchTerm) ||
                    ContainsIgnoreCase(vehicle.Model, searchTerm) ||
                    ContainsIgnoreCase(vehicle.Colour, searchTerm) ||
                    ContainsIgnoreCase(vehicle.VehicleType, searchTerm))
                    .ToList();
            }

            if (model.SearchIncludesAccommodations || model.SearchIncludesAll)
            {
                var allAccommodations = await _repositoryWrapper.Accommodation.FindAll().ToListAsync();
                model.Accommodations = allAccommodations.Where(accommodation =>
                    ContainsIgnoreCase(accommodation.Name, searchTerm) ||
                    ContainsIgnoreCase(accommodation.Address, searchTerm))
                    .ToList();
            }

            if (model.SearchIncludesUnits || model.SearchIncludesAll)
            {
                var allUnits = await _repositoryWrapper.Unit.FindAll()
                    .Include(unit => unit.Accommodation)
                    .ToListAsync();

                model.Units = allUnits.Where(unit =>
                    ContainsIgnoreCase(unit.UnitNumber, searchTerm) ||
                    ContainsIgnoreCase(unit.Accommodation?.Name, searchTerm))
                    .ToList();
            }

            return View(model);
        }

        private static bool ContainsIgnoreCase(string? value, string searchTerm)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase);
        }
    }
}
