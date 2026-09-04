using System.Security.Claims;
using System.Text;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly NotificationService _notificationService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuditService _auditService;
        private readonly AppDbContext _context;
        private readonly TariffCalculationService _tariffCalculationService;

        public PaymentController(
            NotificationService notificationService,
            UserManager<ApplicationUser> userManager,
            AuditService auditService,
            AppDbContext context,
            TariffCalculationService tariffCalculationService)
        {
            _notificationService = notificationService;
            _userManager = userManager;
            _auditService = auditService;
            _context = context;
            _tariffCalculationService = tariffCalculationService;
        }

        [Authorize(Roles = "Student,Staff")]
        public IActionResult MyBills()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                // Persist the latest registered-vehicle charges before the customer sees
                // the bill, rather than only showing an informational vehicle message.
                _tariffCalculationService.RefreshVehicleChargesForCurrentBill(userId);
            }

            var bills = GetOwnedBills().OrderByDescending(bill => bill.Year).ThenByDescending(bill => bill.Month).ToList();
            return View(bills);
        }

        public IActionResult Index()
        {
            var canVerifyPayments = HasPaymentVerificationAccess();
            ViewData["CanVerifyPayments"] = canVerifyPayments;

            IQueryable<Payment> query = _context.Payments
                .Include(payment => payment.Allocations)
                .ThenInclude(allocation => allocation.Bill)
                .OrderByDescending(payment => payment.PaymentDate);

            if (!canVerifyPayments)
            {
                var billIds = GetOwnedBills().Select(bill => bill.Id).ToHashSet();
                query = query.Where(payment =>
                    (payment.BillId.HasValue && billIds.Contains(payment.BillId.Value)) ||
                    payment.Allocations.Any(allocation => billIds.Contains(allocation.BillId)));
            }

            return View(query.ToList());
        }

        public IActionResult Details(int id)
        {
            var payment = _context.Payments
                .Include(item => item.Allocations)
                .ThenInclude(allocation => allocation.Bill)
                .FirstOrDefault(item => item.Id == id);

            if (payment == null)
            {
                return NotFound();
            }

            return !CanAccessPayment(payment) ? Forbid() : View(payment);
        }

        [HttpGet]
        [Authorize(Roles = "Student,Staff")]
        public IActionResult Process()
        {
            return View(BuildFlexiblePaymentModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student,Staff")]
        [ActionName("Process")]
        public IActionResult ProcessConfirm(FlexiblePaymentViewModel model)
        {
            var selectedItems = model.Items
                .Where(item => item.IsSelected)
                .GroupBy(item => new { item.BillId, item.ChargeType })
                .Select(group => group.First())
                .ToList();

            if (!selectedItems.Any())
            {
                ModelState.AddModelError(string.Empty, "Select at least one rent, utility, parking, or vehicle charge before submitting a payment.");
            }

            var ownedBills = GetOwnedBills().ToDictionary(bill => bill.Id);
            var selectedAllocations = new List<PaymentAllocation>();

            foreach (var item in selectedItems)
            {
                if (!ownedBills.TryGetValue(item.BillId, out var bill))
                {
                    ModelState.AddModelError(string.Empty, "One or more selected items are not available for your account.");
                    continue;
                }

                var chargeAmount = GetChargeAmount(bill, item.ChargeType);
                var alreadyAllocated = GetActiveAllocatedAmount(bill.Id, item.ChargeType);
                var outstanding = Math.Max(0, chargeAmount - alreadyAllocated);

                if (outstanding <= 0)
                {
                    ModelState.AddModelError(string.Empty, $"{GetChargeLabel(item.ChargeType)} for {GetBillingPeriod(bill)} is already paid or awaiting verification.");
                    continue;
                }

                selectedAllocations.Add(new PaymentAllocation
                {
                    BillId = bill.Id,
                    ChargeType = item.ChargeType,
                    Amount = outstanding
                });
            }

            if (!ModelState.IsValid || !selectedAllocations.Any())
            {
                var retryModel = BuildFlexiblePaymentModel();
                var selectedKeys = selectedItems.Select(item => (item.BillId, item.ChargeType)).ToHashSet();
                foreach (var item in retryModel.Items)
                {
                    item.IsSelected = selectedKeys.Contains((item.BillId, item.ChargeType));
                }
                return View(retryModel);
            }

            var payment = new Payment
            {
                // Retain a legacy BillId only when the transaction covers exactly one bill.
                BillId = selectedAllocations.Select(allocation => allocation.BillId).Distinct().Count() == 1
                    ? selectedAllocations.First().BillId
                    : null,
                TransactionReferenceId = $"PAY-{Guid.NewGuid():N}".ToUpperInvariant(),
                Amount = selectedAllocations.Sum(allocation => allocation.Amount),
                Method = model.Method,
                Status = PaymentStatus.Pending,
                PaymentDate = DateTime.UtcNow
            };

            _context.Payments.Add(payment);
            _context.SaveChanges();

            foreach (var allocation in selectedAllocations)
            {
                allocation.PaymentId = payment.Id;
                _context.PaymentAllocations.Add(allocation);
            }
            _context.SaveChanges();

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            _notificationService.CreatePaymentPendingNotification(userId, payment.Id, payment.Amount);
            _auditService.Record(
                User,
                "Create",
                "Payment",
                payment.Id,
                $"Submitted {payment.Amount:C} across {selectedAllocations.Count} selected billing item(s); status is Pending verification.");

            TempData["StatusMessage"] = "Your selected payment items have been submitted for verification.";
            return RedirectToAction(nameof(Details), new { id = payment.Id });
        }

        public async Task<IActionResult> Receipt(int id)
        {
            var receipt = await GetReceiptAsync(id);
            if (receipt == null)
            {
                return NotFound();
            }
            if (receipt.Payment.Status != PaymentStatus.Successful)
            {
                TempData["StatusMessage"] = "A receipt is available after the payment has been verified successfully.";
                return RedirectToAction(nameof(Details), new { id });
            }
            return View(receipt);
        }

        public async Task<IActionResult> DownloadReceipt(int id)
        {
            var receipt = await GetReceiptAsync(id);
            if (receipt == null || receipt.Payment.Status != PaymentStatus.Successful)
            {
                return NotFound();
            }

            var allocationLines = string.Join(
                Environment.NewLine,
                receipt.Allocations.Select(allocation =>
                    $"- {GetBillingPeriod(allocation.Bill)} | {GetChargeLabel(allocation.ChargeType)} | {allocation.Amount:C}"));

            var content = $"BRICKSANDBOOKS DIGITAL RECEIPT\n" +
                          $"Receipt reference: {receipt.Payment.TransactionReferenceId}\n" +
                          $"Status: {receipt.Payment.Status}\n" +
                          $"Customer: {receipt.Customer?.FirstName} {receipt.Customer?.LastName}\n" +
                          $"Student/Employee number: {receipt.CustomerIdentifier}\n" +
                          $"Accommodation: {receipt.Accommodation?.Name ?? "Not recorded"}\n" +
                          $"Unit: {receipt.Unit?.UnitNumber ?? "Not recorded"}\n\n" +
                          $"PAID ITEMS\n{allocationLines}\n\n" +
                          $"Total amount: {receipt.Payment.Amount:C}\n" +
                          $"Payment method: {receipt.Payment.Method}\n" +
                          $"Payment date: {receipt.Payment.PaymentDate.ToLocalTime():dd MMM yyyy HH:mm}\n";

            return File(Encoding.UTF8.GetBytes(content), "text/plain", $"Receipt-{receipt.Payment.TransactionReferenceId}.txt");
        }

        [Authorize]
        public IActionResult Verify(int id)
        {
            if (!HasPaymentVerificationAccess())
            {
                return Forbid();
            }

            var payment = _context.Payments
                .Include(item => item.Allocations)
                .ThenInclude(allocation => allocation.Bill)
                .FirstOrDefault(item => item.Id == id);
            if (payment == null)
            {
                return NotFound();
            }
            if (payment.Status != PaymentStatus.Pending)
            {
                TempData["StatusMessage"] = "This payment has already been reviewed and cannot be verified again.";
                return RedirectToAction(nameof(Index));
            }
            return View(payment);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public IActionResult Verify(int id, PaymentStatus status)
        {
            if (!HasPaymentVerificationAccess())
            {
                return Forbid();
            }

            var payment = _context.Payments
                .Include(item => item.Allocations)
                .FirstOrDefault(item => item.Id == id);
            if (payment == null)
            {
                return NotFound();
            }
            if (payment.Status != PaymentStatus.Pending)
            {
                TempData["StatusMessage"] = "This payment has already been reviewed and cannot be verified again.";
                return RedirectToAction(nameof(Index));
            }

            var previousStatus = payment.Status;
            payment.Status = status;
            payment.UpdatedAt = DateTime.UtcNow;

            _context.Payments.Update(payment);
            _context.SaveChanges();

            foreach (var billId in payment.Allocations.Select(allocation => allocation.BillId).Distinct())
            {
                RecalculateBillStatus(billId);
            }

            var customerUserId = GetPaymentCustomerUserId(payment);
            if (!string.IsNullOrWhiteSpace(customerUserId))
            {
                if (status == PaymentStatus.Successful)
                {
                    _notificationService.CreatePaymentSuccessfulNotification(customerUserId, payment.Id, payment.Amount);
                }
                else if (status == PaymentStatus.Failed)
                {
                    _notificationService.CreatePaymentFailedNotification(customerUserId, payment.Id, payment.Amount);
                }
                else
                {
                    _notificationService.CreateActivityNotification(customerUserId, "Payment reviewed", $"Your payment of {payment.Amount:C} was reviewed and marked {status}.", $"/Payment/Details/{payment.Id}", payment.Id);
                }
            }

            if (User.IsInRole("FinancialOfficer") && !User.IsInRole("Admin"))
            {
                _notificationService.CreateRoleActivityNotification("Admin", "Payment reviewed", $"Financial Officer {User.Identity?.Name} marked payment {payment.TransactionReferenceId} as {status}.", $"/Payment/Details/{payment.Id}", payment.Id);
            }

            _auditService.Record(
                User,
                "Verify",
                "Payment",
                payment.Id,
                $"Changed payment status from {previousStatus} to {status} for {payment.Allocations.Count} selected billing item(s).");

            return RedirectToAction(nameof(Index));
        }

        private string? GetPaymentCustomerUserId(Payment payment)
        {
            var billIds = payment.Allocations.Select(allocation => allocation.BillId).ToList();
            if (!billIds.Any() && payment.BillId.HasValue)
            {
                billIds.Add(payment.BillId.Value);
            }

            var unitId = _context.Bills
                .Where(bill => billIds.Contains(bill.Id))
                .Select(bill => (int?)bill.UnitId)
                .FirstOrDefault();
            if (!unitId.HasValue)
            {
                return null;
            }

            return _context.Occupancies
                .Where(occupancy => occupancy.UnitId == unitId.Value)
                .OrderByDescending(occupancy => occupancy.EndDate == null)
                .ThenByDescending(occupancy => occupancy.StartDate)
                .Select(occupancy => occupancy.UserId)
                .FirstOrDefault();
        }

        private FlexiblePaymentViewModel BuildFlexiblePaymentModel()
        {
            var model = new FlexiblePaymentViewModel();
            foreach (var bill in GetOwnedBills().OrderBy(bill => bill.Year).ThenBy(bill => bill.Month))
            {
                foreach (var chargeType in Enum.GetValues<BillChargeType>())
                {
                    var amount = GetChargeAmount(bill, chargeType);
                    if (amount <= 0)
                    {
                        continue;
                    }

                    var alreadyAllocated = GetActiveAllocatedAmount(bill.Id, chargeType);
                    if (alreadyAllocated >= amount)
                    {
                        continue;
                    }

                    model.Items.Add(new FlexiblePaymentItemViewModel
                    {
                        BillId = bill.Id,
                        ChargeType = chargeType,
                        ChargeLabel = GetChargeLabel(chargeType),
                        BillingPeriod = GetBillingPeriod(bill),
                        DueDate = bill.DueDate,
                        Amount = amount,
                        AlreadyAllocated = alreadyAllocated
                    });
                }
            }
            return model;
        }

        private IEnumerable<Bill> GetOwnedBills()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Enumerable.Empty<Bill>();
            }

            var activeOccupancy = _context.Occupancies
                .Where(occupancy => occupancy.UserId == userId
                    && (occupancy.EndDate == null || occupancy.EndDate >= DateTime.UtcNow))
                .OrderByDescending(occupancy => occupancy.StartDate)
                .FirstOrDefault();
            if (activeOccupancy == null)
            {
                return Enumerable.Empty<Bill>();
            }

            return _context.Bills
                .Where(bill => bill.UnitId == activeOccupancy.UnitId && bill.UserId == userId)
                .ToList();
        }

        private decimal GetActiveAllocatedAmount(int billId, BillChargeType chargeType)
        {
            return GetAllocatedAmount(billId, chargeType, includePendingPayments: true);
        }

        private decimal GetSuccessfulAllocatedAmount(int billId, BillChargeType chargeType)
        {
            return GetAllocatedAmount(billId, chargeType, includePendingPayments: false);
        }

        private decimal GetAllocatedAmount(int billId, BillChargeType chargeType, bool includePendingPayments)
        {
            var allocationAmount = _context.PaymentAllocations
                .Where(allocation => allocation.BillId == billId
                    && allocation.ChargeType == chargeType
                    && (allocation.Payment.Status == PaymentStatus.Successful
                        || (includePendingPayments && allocation.Payment.Status == PaymentStatus.Pending)))
                .Sum(allocation => (decimal?)allocation.Amount) ?? 0m;

            // Older payments may use Payment.BillId without PaymentAllocations. Allocate
            // those legacy payments across rent, water, electricity, parking, then extra
            // vehicles so reopening a bill for a newly added car does not charge rent again.
            var legacyPaymentAmount = _context.Payments
                .Where(payment => payment.BillId == billId
                    && !payment.Allocations.Any()
                    && (payment.Status == PaymentStatus.Successful
                        || (includePendingPayments && payment.Status == PaymentStatus.Pending)))
                .Sum(payment => (decimal?)payment.Amount) ?? 0m;
            if (legacyPaymentAmount <= 0)
            {
                return allocationAmount;
            }

            var bill = _context.Bills.FirstOrDefault(item => item.Id == billId);
            if (bill == null)
            {
                return allocationAmount;
            }

            var remainingLegacyAmount = legacyPaymentAmount;
            foreach (var type in new[]
            {
                BillChargeType.Rent,
                BillChargeType.Water,
                BillChargeType.Electricity,
                BillChargeType.Parking,
                BillChargeType.AdditionalVehicle
            })
            {
                var amountForType = Math.Min(remainingLegacyAmount, GetChargeAmount(bill, type));
                if (type == chargeType)
                {
                    return allocationAmount + amountForType;
                }

                remainingLegacyAmount = Math.Max(0, remainingLegacyAmount - amountForType);
            }

            return allocationAmount;
        }

        private void RecalculateBillStatus(int billId)
        {
            var bill = _context.Bills.FirstOrDefault(item => item.Id == billId);
            if (bill == null)
            {
                return;
            }

            var payableChargeTypes = Enum.GetValues<BillChargeType>()
                .Where(chargeType => GetChargeAmount(bill, chargeType) > 0)
                .ToList();
            var isFullyPaid = payableChargeTypes.Any()
                && payableChargeTypes.All(chargeType => GetSuccessfulAllocatedAmount(bill.Id, chargeType) >= GetChargeAmount(bill, chargeType));

            bill.Status = isFullyPaid ? BillStatus.Paid : BillStatus.Pending;
            bill.UpdatedAt = DateTime.UtcNow;
            _context.Bills.Update(bill);
            _context.SaveChanges();
        }

        private async Task<PaymentReceiptViewModel?> GetReceiptAsync(int id)
        {
            var payment = _context.Payments
                .Include(item => item.Allocations)
                .ThenInclude(allocation => allocation.Bill)
                .FirstOrDefault(item => item.Id == id);
            if (payment == null || !CanAccessPayment(payment))
            {
                return null;
            }

            var firstBill = payment.Allocations.Select(allocation => allocation.Bill).FirstOrDefault()
                ?? (payment.BillId.HasValue ? _context.Bills.FirstOrDefault(bill => bill.Id == payment.BillId.Value) : null);
            var unit = firstBill == null ? null : _context.Units.FirstOrDefault(item => item.Id == firstBill.UnitId);
            var accommodation = unit == null ? null : _context.Accommodations.FirstOrDefault(item => item.Id == unit.AccommodationId);
            var occupancy = unit == null
                ? null
                : _context.Occupancies
                    .Where(item => item.UnitId == unit.Id)
                    .OrderByDescending(item => item.StartDate)
                    .FirstOrDefault();
            var customer = occupancy == null ? await _userManager.GetUserAsync(User) : await _userManager.FindByIdAsync(occupancy.UserId);

            return new PaymentReceiptViewModel
            {
                Payment = payment,
                Allocations = payment.Allocations.OrderBy(allocation => allocation.Bill.Year).ThenBy(allocation => allocation.Bill.Month).ThenBy(allocation => allocation.ChargeType).ToList(),
                Unit = unit,
                Accommodation = accommodation,
                Customer = customer
            };
        }

        private bool HasPaymentVerificationAccess()
        {
            if (User.IsInRole("Admin") || User.IsInRole("FinancialOfficer"))
            {
                return true;
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return !string.IsNullOrWhiteSpace(userId)
                && _context.Users.Any(user => user.Id == userId && (user.IsAdmin || user.IsFinancialOfficer));
        }

        private bool CanAccessPayment(Payment payment)
        {
            if (HasPaymentVerificationAccess())
            {
                return true;
            }

            var ownedBillIds = GetOwnedBills().Select(bill => bill.Id).ToHashSet();
            return (payment.BillId.HasValue && ownedBillIds.Contains(payment.BillId.Value))
                || payment.Allocations.Any(allocation => ownedBillIds.Contains(allocation.BillId));
        }

        private static decimal GetChargeAmount(Bill bill, BillChargeType chargeType)
        {
            return chargeType switch
            {
                BillChargeType.Rent => bill.RentAmount,
                BillChargeType.Water => bill.WaterAmount,
                BillChargeType.Electricity => bill.ElectricityAmount,
                BillChargeType.Parking => bill.ParkingAmount,
                BillChargeType.AdditionalVehicle => bill.AdditionalVehicleAmount,
                _ => 0m
            };
        }

        private static string GetChargeLabel(BillChargeType chargeType)
        {
            return chargeType switch
            {
                BillChargeType.Rent => "Rent",
                BillChargeType.Water => "Water",
                BillChargeType.Electricity => "Electricity",
                BillChargeType.Parking => "Parking",
                BillChargeType.AdditionalVehicle => "Additional vehicle",
                _ => "Other charge"
            };
        }

        private static string GetBillingPeriod(Bill bill) => new DateTime(bill.Year, bill.Month, 1).ToString("MMMM yyyy");
    }
}
