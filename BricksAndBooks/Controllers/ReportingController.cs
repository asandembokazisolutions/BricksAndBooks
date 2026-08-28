using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Data;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using BricksAndBooks.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace BricksAndBooks.Controllers
{
    [Authorize(Roles = "Admin,FinancialOfficer")] // Admins and financial officers can view reports
    public class ReportingController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService _notificationService;

        public ReportingController(IRepositoryWrapper repositoryWrapper, UserManager<ApplicationUser> userManager, NotificationService notificationService)
        {
            _repositoryWrapper = repositoryWrapper;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        // GET: Reporting
        public async Task<IActionResult> Index()
        {
            // We'll show a list of available reports
            return View();
        }

        // GET: Reporting/Revenue
        public async Task<IActionResult> Revenue()
        {
            // We can group payments by month and year to show revenue over time
            var revenueData = _repositoryWrapper.Payment.FindAll()
                .Where(p => p.Status == PaymentStatus.Successful)
                .GroupBy(p => new { p.PaymentDate.Year, p.PaymentDate.Month })
                .Select(g => new RevenueReportViewModel
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TotalAmount = g.Sum(p => p.Amount)
                })
                .OrderByDescending(r => r.Year)
                .ThenByDescending(r => r.Month)
                .ToList();

            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                _notificationService.CreateActivityNotification(user.Id, "Revenue report generated", "You generated the latest successful-payment revenue report.", "/Reporting/Revenue");
            }
            return View(revenueData);
        }

        // GET: Reporting/Occupancy
        public async Task<IActionResult> Occupancy()
        {
            // We can calculate occupancy rates per accommodation
            var occupancyData = _repositoryWrapper.Accommodation.FindAll()
                .Select(a => new OccupancyReportViewModel
                {
                    AccommodationName = a.Name,
                    TotalUnits = a.Units.Count,
                    OccupiedUnits = a.Units.Count(u => !u.IsAvailable), // Assuming we have an IsAvailable property on Unit
                    OccupancyRate = a.Units.Count > 0 ? (double)a.Units.Count(u => !u.IsAvailable) / a.Units.Count * 100 : 0
                })
                .ToList();

            var user = await _userManager.GetUserAsync(User);
            if (user != null)
            {
                _notificationService.CreateActivityNotification(user.Id, "Occupancy report generated", "You generated the latest accommodation occupancy report.", "/Reporting/Occupancy");
            }
            return View(occupancyData);
        }

        // We can add more reports as needed, such as payment method breakdown, etc.
    }
}