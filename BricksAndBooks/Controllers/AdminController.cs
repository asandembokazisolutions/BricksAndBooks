using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using BricksAndBooks.Data;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using BricksAndBooks.Infrastructure;

namespace BricksAndBooks.Controllers
{
    [Authorize(Roles = "Admin")] // Only admins
    public class AdminController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly TariffCalculationService _tariffCalculationService;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminController(IRepositoryWrapper repositoryWrapper, TariffCalculationService tariffCalculationService, UserManager<ApplicationUser> userManager)
        {
            _repositoryWrapper = repositoryWrapper;
            _tariffCalculationService = tariffCalculationService;
            _userManager = userManager;
        }

        // GET: Admin (Dashboard)
        public async Task<IActionResult> Index()
        {
            // Stats
            var totalAccommodations = _repositoryWrapper.Accommodation.FindAll().Count();
            var totalUnits = _repositoryWrapper.Unit.FindAll().Count();
            var totalBills = _repositoryWrapper.Bill.FindAll().Count();
            var totalPayments = _repositoryWrapper.Payment.FindAll().Count();
            var pendingPayments = _repositoryWrapper.Payment.FindByCondition(p => p.Status == Models.PaymentStatus.Pending).Count();
            var totalUsers = _userManager.Users.Count();
            var students = await _userManager.GetUsersInRoleAsync("Student");
            var staff = await _userManager.GetUsersInRoleAsync("Staff");
            var financialOfficers = await _userManager.GetUsersInRoleAsync("FinancialOfficer");
            var occupiedUnits = _repositoryWrapper.Unit.FindByCondition(u => !u.IsAvailable).Count();
            var totalRevenue = _repositoryWrapper.Payment.FindByCondition(p => p.Status == PaymentStatus.Successful).Sum(p => p.Amount);
            var outstandingBalance = _repositoryWrapper.Bill
    .FindByCondition(b => b.Status == BillStatus.Pending)
    .Sum(b => b.RentAmount + b.WaterAmount + b.ElectricityAmount + b.ParkingAmount + b.AdditionalVehicleAmount);

            var dashboardModel = new DashboardViewModel
            {
                TotalAccommodations = totalAccommodations,
                TotalUnits = totalUnits,
                TotalBills = totalBills,
                TotalPayments = totalPayments,
                PendingPayments = pendingPayments,
                TotalUsers = totalUsers,
                TotalStudents = students.Count,
                TotalStaff = staff.Count,
                TotalFinancialOfficers = financialOfficers.Count,
                OccupiedUnits = occupiedUnits,
                AvailableUnits = totalUnits - occupiedUnits,
                TotalRevenue = totalRevenue,
                OutstandingBalance = outstandingBalance
            };

            // Chart data: Revenue for the last 6 months
            var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
            var revenueData = _repositoryWrapper.Payment.FindAll()
                .Where(p => p.Status == PaymentStatus.Successful && p.PaymentDate >= sixMonthsAgo)
                .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
                .Select(g => new RevenueReportViewModel
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TotalAmount = g.Sum(p => p.Amount)
                })
                .OrderBy(r => r.Year)
                .ThenBy(r => r.Month)
                .ToList() ?? new List<RevenueReportViewModel>();

            // Prepare data for Chart.js: labels and data array
            var revenueLabels = new List<string>();
            var revenueValues = new List<decimal>();
            if (revenueData != null)
            {
                foreach (var rev in revenueData)
                {
                    var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(rev.Month);
                    revenueLabels.Add($"{monthName} {rev.Year}");
                    revenueValues.Add(rev.TotalAmount);
                }
            }

            // Chart data: Occupancy per accommodation
            var occupancyData = _repositoryWrapper.Accommodation.FindAll()
                .Select(a => new OccupancyReportViewModel
                {
                    AccommodationName = a.Name ?? string.Empty,
                    TotalUnits = a.Units.Count,
                    OccupiedUnits = a.Units.Count(u => !u.IsAvailable),
                    OccupancyRate = a.Units.Count > 0 ? (double)a.Units.Count(u => !u.IsAvailable) / a.Units.Count * 100 : 0
                })
                .ToList() ?? new List<OccupancyReportViewModel>();

            var occupancyLabels = new List<string>();
            var occupancyValues = new List<double>();
            if (occupancyData != null)
            {
                foreach (var occ in occupancyData)
                {
                    occupancyLabels.Add(occ.AccommodationName ?? string.Empty);
                    occupancyValues.Add(occ.OccupancyRate);
                }
            }

            // Pass data to view via ViewBag
            ViewBag.RevenueLabels = revenueLabels;
            ViewBag.RevenueValues = revenueValues;
            ViewBag.OccupancyLabels = occupancyLabels;
            ViewBag.OccupancyValues = occupancyValues;

            return View(dashboardModel);
        }

        // GET: Admin/TariffConfig
        public async Task<IActionResult> TariffConfig()
        {
            var tariffs = _repositoryWrapper.Tariff.FindAll().ToList();
            return View(tariffs);
        }

        // GET: Admin/TariffConfig/Create
        public IActionResult TariffConfigCreate()
        {
            return View();
        }

        // POST: Admin/TariffConfig/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TariffConfigCreate(Tariff tariff)
        {
            if (ModelState.IsValid)
            {
                // Ensure EffectiveFrom is set
                if (tariff.EffectiveFrom == DateTime.MinValue)
                {
                    tariff.EffectiveFrom = DateTime.UtcNow;
                }

                _repositoryWrapper.Tariff.Create(tariff);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(TariffConfig));
            }
            return View(tariff);
        }

        // GET: Admin/TariffConfig/Edit/5
        public async Task<IActionResult> TariffConfigEdit(int id)
        {
            var tariff = _repositoryWrapper.Tariff.FindByCondition(t => t.Id == id).FirstOrDefault();
            if (tariff == null)
            {
                return NotFound();
            }
            return View(tariff);
        }

        // POST: Admin/TariffConfig/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TariffConfigEdit(int id, TariffType type, decimal amount, string description)
        {
            var tariff = _repositoryWrapper.Tariff.FindByCondition(t => t.Id == id).FirstOrDefault();
            if (tariff == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                tariff.Type = type;
                tariff.Amount = amount;
                tariff.Description = description;
                tariff.UpdatedAt = System.DateTime.UtcNow;
                _repositoryWrapper.Tariff.Update(tariff);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(TariffConfig));
            }
            return View(tariff);
        }

        // GET: Admin/TariffConfig/Delete/5
        public async Task<IActionResult> TariffConfigDelete(int id)
        {
            var tariff = _repositoryWrapper.Tariff.FindByCondition(t => t.Id == id).FirstOrDefault();
            if (tariff == null)
            {
                return NotFound();
            }
            return View(tariff);
        }

        // POST: Admin/TariffConfig/Delete/5
        [HttpPost, ActionName("TariffConfigDelete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TariffConfigDeleteConfirmed(int id)
        {
            var tariff = _repositoryWrapper.Tariff.FindByCondition(t => t.Id == id).FirstOrDefault();
            if (tariff != null)
            {
                _repositoryWrapper.Tariff.Delete(tariff);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(TariffConfig));
            }
            return RedirectToAction(nameof(TariffConfig));
        }

        // GET: Admin/Reports
        public IActionResult Reports()
        {
            return RedirectToAction("Index", "Reporting");
        }

        // GET: Admin/CalculateBills
        public IActionResult CalculateBills()
        {
            return View();
        }

        // POST: Admin/CalculateBills
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CalculateBills(int year, int month)
        {
            if (ModelState.IsValid)
            {
                // Validate month and year
                if (month < 1 || month > 12)
                {
                    ModelState.AddModelError(string.Empty, "Month must be between 1 and 12.");
                    return View();
                }
                if (year < 2000)
                {
                    ModelState.AddModelError(string.Empty, "Year must be valid.");
                    return View();
                }

                // Calculate bills
                _tariffCalculationService.CalculateBills(year, month);

                // Show a success message
                ViewBag.Message = $"Bills for {new System.Globalization.CultureInfo("en-US").DateTimeFormat.GetMonthName(month)} {year} have been calculated and created.";
                return View();
            }
            return View();
        }
    }
}