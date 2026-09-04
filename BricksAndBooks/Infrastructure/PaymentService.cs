using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BricksAndBooks.Data;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;
using BricksAndBooks.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Infrastructure
{
    public class PaymentService
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService _notificationService;
        private readonly AuditService _auditService;
        private readonly TariffCalculationService _tariffCalculationService;

        public PaymentService(AppDbContext context, UserManager<ApplicationUser> userManager, NotificationService notificationService, AuditService auditService, TariffCalculationService tariffCalculationService)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
            _auditService = auditService;
            _tariffCalculationService = tariffCalculationService;
        }

        // Business logic methods extracted from PaymentController

        public string? GetPaymentCustomerUserId(Payment payment)
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

        public FlexiblePaymentViewModel BuildFlexiblePaymentModel(string userId)
        {
            var model = new FlexiblePaymentViewModel();
            var bills = GetOwnedBills(userId).OrderBy(bill => bill.Year).ThenBy(bill => bill.Month);
            foreach (var bill in bills)
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

        public IEnumerable<Bill> GetOwnedBills(string userId)
        {
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

        public decimal GetActiveAllocatedAmount(int billId, BillChargeType chargeType)
        {
            return GetAllocatedAmount(billId, chargeType, includePendingPayments: true);
        }

        public decimal GetSuccessfulAllocatedAmount(int billId, BillChargeType chargeType)
        {
            return GetAllocatedAmount(billId, chargeType, includePendingPayments: false);
        }

        public decimal GetAllocatedAmount(int billId, BillChargeType chargeType, bool includePendingPayments)
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

        public void RecalculateBillStatus(int billId)
        {
            var bill = _context.Bills.FirstOrDefault(item => item.Id == billId);
            if (bill == null)
            {
                return;
            }

            var isFullyPaid = Enum.GetValues<BillChargeType>()
                .Where(chargeType => GetChargeAmount(bill, chargeType) > 0)
                .All(chargeType => GetSuccessfulAllocatedAmount(bill.Id, chargeType) >= GetChargeAmount(bill, chargeType));

            bill.Status = isFullyPaid ? BillStatus.Paid : BillStatus.Pending;
            bill.UpdatedAt = DateTime.UtcNow;
            _context.Bills.Update(bill);
            _context.SaveChanges();
        }

        public async Task<PaymentReceiptViewModel?> GetReceiptAsync(int id, string userId, bool canVerifyPayments)
        {
            var payment = _context.Payments
                .Include(item => item.Allocations)
                .ThenInclude(allocation => allocation.Bill)
                .FirstOrDefault(item => item.Id == id);
            if (payment == null || !CanAccessPayment(payment, userId, canVerifyPayments))
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
            var customer = occupancy == null ? await _userManager.FindByIdAsync(userId) : await _userManager.FindByIdAsync(occupancy.UserId);

            return new PaymentReceiptViewModel
            {
                Payment = payment,
                Allocations = payment.Allocations.OrderBy(allocation => allocation.Bill.Year).ThenBy(allocation => allocation.Bill.Month).ThenBy(allocation => allocation.ChargeType).ToList(),
                Unit = unit,
                Accommodation = accommodation,
                Customer = customer
            };
        }

        public bool HasPaymentVerificationAccess(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            // Check roles via claims (faster than database lookup for role checks)
            // Actually, let's check the database for IsAdmin/IsFinancialOfficer flags as in original code
            return _context.Users.Any(user => user.Id == userId && (user.IsAdmin || user.IsFinancialOfficer));
        }

        public bool CanAccessPayment(Payment payment, string userId, bool canVerifyPayments)
        {
            if (canVerifyPayments)
            {
                return true;
            }

            var ownedBillIds = GetOwnedBills(userId).Select(bill => bill.Id).ToHashSet();
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
