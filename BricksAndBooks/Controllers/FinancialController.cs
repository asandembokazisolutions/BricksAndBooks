using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Data;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;

namespace BricksAndBooks.Controllers
{
    [Authorize(Roles = "FinancialOfficer,Admin")] // Financial officers and admins can access
    public class FinancialController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager;

        public FinancialController(IRepositoryWrapper repositoryWrapper, UserManager<ApplicationUser> userManager)
        {
            _repositoryWrapper = repositoryWrapper;
            _userManager = userManager;
        }

        // GET: Financial
        public async Task<IActionResult> Index()
        {
            var payments = _repositoryWrapper.Payment.FindAll().ToList();
            var successfulPayments = payments.Where(p => p.Status == PaymentStatus.Successful).ToList();
            var pendingPayments = payments.Where(p => p.Status == PaymentStatus.Pending).ToList();
            var failedPayments = payments.Where(p => p.Status == PaymentStatus.Failed).ToList();

            var students = await _userManager.GetUsersInRoleAsync("Student");
            var staff = await _userManager.GetUsersInRoleAsync("Staff");
            var customers = students.Concat(staff).Select(u => u.Id).Distinct().Count();

            var model = new FinancialViewModel
            {
                TotalRevenue = successfulPayments.Sum(p => p.Amount),
                TotalPending = pendingPayments.Sum(p => p.Amount),
                TotalFailed = failedPayments.Sum(p => p.Amount),
                SuccessfulPaymentCount = successfulPayments.Count,
                PendingPaymentCount = pendingPayments.Count,
                FailedPaymentCount = failedPayments.Count,
                CustomerCount = customers,
                OutstandingBills = _repositoryWrapper.Bill.FindByCondition(b => b.Status == BillStatus.Pending).Sum(b => b.RentAmount + b.WaterAmount + b.ElectricityAmount + b.ParkingAmount + b.AdditionalVehicleAmount),
                RecentPayments = payments.OrderByDescending(p => p.PaymentDate).Take(8).ToList()
            };

            return View(model);
        }

        // GET: Financial/Customers
        public async Task<IActionResult> Customers()
        {
            // We want to list customers (students and staff) but not financial officers or admins?
            // We'll get all users that are either students or staff.
            var users = await _userManager.GetUsersInRoleAsync("Student");
            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var customers = users.Concat(staffUsers)
                .GroupBy(u => u.Id)
                .Select(g => g.First())
                .ToList();
            return View(customers);
        }

        // GET: Financial/Customer/Details/5
        public async Task<IActionResult> CustomerDetails(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // We might want to show the user's bills and payments
            // Get the units that the user has occupied (current or past) via Occupancy
            var occupiedUnitIds = _repositoryWrapper.Occupancy.FindAll()
                .Where(o => o.UserId == id)
                .Select(o => o.UnitId)
                .ToList();

            var userBills = _repositoryWrapper.Bill.FindAll()
                .Where(b => occupiedUnitIds.Contains(b.UnitId))
                .ToList();

            var model = new CustomerFinancialViewModel
            {
                User = user,
                Bills = userBills
            };

            return View(model);
        }

        // We'll create the view models later.

        // GET: Financial/Transactions
        public async Task<IActionResult> Transactions()
        {
            var transactions = _repositoryWrapper.Payment.FindAll().ToList();
            return View(transactions);
        }

        // GET: Financial/Transaction/Details/5
        public async Task<IActionResult> TransactionDetails(int id)
        {
            var transaction = _repositoryWrapper.Payment.FindByCondition(p => p.Id == id).FirstOrDefault();
            if (transaction == null)
            {
                return NotFound();
            }
            return View(transaction);
        }
    }
}