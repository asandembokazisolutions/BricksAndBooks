using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Data;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;

using BricksAndBooks.Models.ViewModels;

namespace BricksAndBooks.Controllers
{
    public class HomeController(ILogger<HomeController> logger, IRepositoryWrapper repositoryWrapper, UserManager<ApplicationUser> userManager, AppDbContext context) : Controller
    {
        private readonly ILogger<HomeController> _logger = logger;
        private readonly IRepositoryWrapper _repositoryWrapper = repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager = userManager;
        private readonly AppDbContext _context = context;

        private decimal GetPayableAmount(Bill bill)
        {
            return Enum.GetValues<BillChargeType>().Sum(type => GetPayableAmount(bill, type));
        }

        private decimal GetPayableAmount(Bill bill, BillChargeType chargeType)
        {
            var amount = chargeType switch
            {
                BillChargeType.Rent => bill.RentAmount,
                BillChargeType.Water => bill.WaterAmount,
                BillChargeType.Electricity => bill.ElectricityAmount,
                BillChargeType.Parking => bill.ParkingAmount,
                BillChargeType.AdditionalVehicle => bill.AdditionalVehicleAmount,
                _ => 0m
            };
            var allocated = _context.PaymentAllocations
                .Include(allocation => allocation.Payment)
                .Where(allocation => allocation.BillId == bill.Id
                    && allocation.ChargeType == chargeType
                    && (allocation.Payment.Status == PaymentStatus.Pending || allocation.Payment.Status == PaymentStatus.Successful))
                .Sum(allocation => (decimal?)allocation.Amount) ?? 0m;
            return Math.Max(0m, amount - allocated);
        }

        public async Task<IActionResult> Index()
        {
            if (User.IsInRole("Admin"))
            {
                return RedirectToAction("Index", "Admin");
            }
            if (User.IsInRole("FinancialOfficer"))
            {
                return RedirectToAction("Index", "Financial");
            }
            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    var latestApplication = _context.AccommodationApplications
                        .Include(a => a.Accommodation)
                        .Where(a => a.UserId == user.Id)
                        .OrderByDescending(a => a.CreatedAt)
                        .FirstOrDefault();
                    var currentOccupancy = _repositoryWrapper.Occupancy.FindAll()
                        .Where(o => o.UserId == user.Id && (o.EndDate == null || o.EndDate >= DateTime.UtcNow))
                        .OrderByDescending(o => o.StartDate)
                        .FirstOrDefault();
                    var unit = currentOccupancy == null ? null : _repositoryWrapper.Unit.FindByCondition(u => u.Id == currentOccupancy.UnitId).FirstOrDefault();
                    var accommodation = unit == null ? null : _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == unit.AccommodationId).FirstOrDefault();
                    var bills = unit == null ? new List<Bill>() : _repositoryWrapper.Bill.FindByCondition(b => b.UnitId == unit.Id)
                        .AsEnumerable()
                        .GroupBy(b => new { b.UnitId, b.Year, b.Month })
                        .Select(group => group.OrderByDescending(b => b.Status == BillStatus.Paid).ThenByDescending(b => b.UpdatedAt).ThenByDescending(b => b.Id).First())
                        .OrderByDescending(b => b.Year).ThenByDescending(b => b.Month).ToList();
                    var pendingBills = bills.Where(b => b.Status == BillStatus.Pending && GetPayableAmount(b) > 0m).ToList();
                    var billIds = bills.Select(b => b.Id).ToHashSet();
                    var payments = _context.Payments
                        .Include(payment => payment.Allocations)
                        .Where(payment =>
                            (payment.BillId.HasValue && billIds.Contains(payment.BillId.Value)) ||
                            payment.Allocations.Any(allocation => billIds.Contains(allocation.BillId)))
                        .OrderByDescending(payment => payment.PaymentDate)
                        .Take(5)
                        .ToList();
                    var model = new CustomerDashboardViewModel
                    {
                        User = user,
                        LatestApplication = latestApplication,
                        Unit = unit,
                        Accommodation = accommodation,
                        OutstandingBalance = pendingBills.Sum(GetPayableAmount),
                        RentDue = pendingBills.Sum(b => GetPayableAmount(b, BillChargeType.Rent)),
                        WaterDue = pendingBills.Sum(b => GetPayableAmount(b, BillChargeType.Water)),
                        ElectricityDue = pendingBills.Sum(b => GetPayableAmount(b, BillChargeType.Electricity)),
                        ParkingDue = pendingBills.Sum(b => GetPayableAmount(b, BillChargeType.Parking) + GetPayableAmount(b, BillChargeType.AdditionalVehicle)),
                        RecentBills = bills.Take(5).ToList(),
                        RecentPayments = payments,
                        UnreadNotifications = _repositoryWrapper.Notification.FindAll().Count(n => n.UserId == user.Id && !n.IsRead),
                        UnreadMessages = _repositoryWrapper.ChatMessage.FindAll().Count(m => m.RecipientUserId == user.Id && !m.IsRead)
                    };
                    return View("CustomerDashboard", model);
                }
            }

            // Public landing-page statistics
            var dashboardModel = new DashboardViewModel
            {
                TotalAccommodations = _repositoryWrapper.Accommodation.FindAll().Count(),
                TotalUnits = _repositoryWrapper.Unit.FindAll().Count(),
                TotalBills = _repositoryWrapper.Bill.FindAll().Count(),
                TotalPayments = _repositoryWrapper.Payment.FindAll().Count(),
                PendingPayments = _repositoryWrapper.Payment.FindByCondition(p => p.Status == BricksAndBooks.Models.PaymentStatus.Pending).Count(),
                FeaturedAccommodations = await _context.Accommodations
                    .AsNoTracking()
                    .Include(accommodation => accommodation.Units)
                    .OrderBy(accommodation => accommodation.Name)
                    .Take(6)
                    .ToListAsync()
            };

            return View(dashboardModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

    }
}
