using System.Collections.Generic;
using System.Linq;
using BricksAndBooks.Data;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Infrastructure
{
    public class PaymentAnomalyDetectionService
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly AppDbContext _context;

        public PaymentAnomalyDetectionService(IRepositoryWrapper repositoryWrapper, AppDbContext context)
        {
            _repositoryWrapper = repositoryWrapper;
            _context = context;
        }

        public List<PaymentAnomalyResult> DetectAnomalies()
        {
            var anomalies = new List<PaymentAnomalyResult>();
            anomalies.AddRange(DetectDuplicateTransactions());
            anomalies.AddRange(DetectRepeatedFailedPayments());
            anomalies.AddRange(DetectSuccessfulPaymentsWithUnpaidBills());
            anomalies.AddRange(DetectSuspiciousPaymentPatterns());
            return anomalies;
        }

        private List<PaymentAnomalyResult> DetectDuplicateTransactions()
        {
            // Fetch first, then group in memory. EF Core cannot translate GroupBy + SelectMany
            // for this provider, while the in-memory grouping is reliable for the Risk Monitor.
            var payments = _context.Payments
                .AsNoTracking()
                .Where(payment => !string.IsNullOrWhiteSpace(payment.TransactionReferenceId))
                .ToList();

            return payments
                .GroupBy(payment => payment.TransactionReferenceId!, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .Select(payment => new PaymentAnomalyResult
                {
                    Payment = payment,
                    AnomalyType = "Duplicate Transaction",
                    Description = $"Transaction reference ID '{payment.TransactionReferenceId}' appears more than once in the system.",
                    Severity = "High"
                })
                .ToList();
        }

        private List<PaymentAnomalyResult> DetectRepeatedFailedPayments()
        {
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            var failedPayments = GetPaymentsWithAllocations()
                .Where(payment => payment.Status == PaymentStatus.Failed && payment.PaymentDate >= sevenDaysAgo)
                .ToList();

            var repeatedPayments = failedPayments
                .Select(payment => new { Payment = payment, UserId = GetPaymentUserId(payment) })
                .Where(item => item.UserId != null)
                .GroupBy(item => item.UserId!)
                .Where(group => group.Count() >= 3)
                .SelectMany(group => group.Select(item => item.Payment));

            return repeatedPayments.Select(payment => new PaymentAnomalyResult
            {
                Payment = payment,
                AnomalyType = "Repeated Failed Payments",
                Description = "User has 3 or more failed payments in the last 7 days.",
                Severity = "Medium"
            }).ToList();
        }

        private List<PaymentAnomalyResult> DetectSuccessfulPaymentsWithUnpaidBills()
        {
            var anomalies = new List<PaymentAnomalyResult>();
            var successfulPayments = GetPaymentsWithAllocations()
                .Where(payment => payment.Status == PaymentStatus.Successful)
                .ToList();

            foreach (var payment in successfulPayments)
            {
                // A flexible payment may intentionally cover only some charges on a bill. A remaining
                // Pending balance is therefore normal and is not an anomaly. Failed/Cancelled bills are not.
                var invalidAllocatedBill = payment.Allocations
                    .Select(allocation => allocation.Bill)
                    .FirstOrDefault(bill => bill.Status is BillStatus.Failed or BillStatus.Cancelled);

                if (invalidAllocatedBill != null)
                {
                    anomalies.Add(new PaymentAnomalyResult
                    {
                        Payment = payment,
                        AnomalyType = "Payment Status Mismatch",
                        Description = $"Payment is marked Successful but allocated bill {invalidAllocatedBill.Month}/{invalidAllocatedBill.Year} is {invalidAllocatedBill.Status}.",
                        Severity = "High"
                    });
                }
                else if (!payment.Allocations.Any() && payment.Bill != null && payment.Bill.Status is BillStatus.Failed or BillStatus.Cancelled)
                {
                    // Retain checks for legacy one-bill payments.
                    anomalies.Add(new PaymentAnomalyResult
                    {
                        Payment = payment,
                        AnomalyType = "Payment Status Mismatch",
                        Description = $"Payment is marked Successful but associated legacy bill is {payment.Bill.Status}.",
                        Severity = "High"
                    });
                }
            }

            return anomalies;
        }

        private List<PaymentAnomalyResult> DetectSuspiciousPaymentPatterns()
        {
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var successfulPayments = GetPaymentsWithAllocations()
                .Where(payment => payment.Status == PaymentStatus.Successful && payment.PaymentDate >= oneHourAgo)
                .ToList();

            var suspiciousPayments = successfulPayments
                .Select(payment => new { Payment = payment, UserId = GetPaymentUserId(payment) })
                .Where(item => item.UserId != null)
                .GroupBy(item => new { item.UserId, item.Payment.Amount })
                .Where(group => group.Count() >= 3)
                .SelectMany(group => group.Select(item => item.Payment));

            return suspiciousPayments.Select(payment => new PaymentAnomalyResult
            {
                Payment = payment,
                AnomalyType = "Suspicious Payment Pattern",
                Description = $"Multiple successful payments of the same amount ({payment.Amount:C}) were submitted by one user within one hour.",
                Severity = "Medium"
            }).ToList();
        }

        private List<Payment> GetPaymentsWithAllocations()
        {
            return _context.Payments
                .Include(payment => payment.Bill)
                .Include(payment => payment.Allocations)
                .ThenInclude(allocation => allocation.Bill)
                .AsNoTracking()
                .ToList();
        }

        private string? GetPaymentUserId(Payment payment)
        {
            var unitId = payment.Allocations.Select(allocation => allocation.Bill.UnitId).FirstOrDefault();
            if (unitId == 0 && payment.Bill != null)
            {
                unitId = payment.Bill.UnitId;
            }
            if (unitId == 0)
            {
                return null;
            }

            return _context.Occupancies
                .Where(occupancy => occupancy.UnitId == unitId && (occupancy.EndDate == null || occupancy.EndDate >= DateTime.UtcNow))
                .OrderByDescending(occupancy => occupancy.StartDate)
                .Select(occupancy => occupancy.UserId)
                .FirstOrDefault();
        }
    }

    public class PaymentAnomalyResult
    {
        public Payment? Payment { get; set; }
        public string? AnomalyType { get; set; }
        public string? Description { get; set; }
        public string? Severity { get; set; }
    }
}
