using System.Linq;
using BricksAndBooks.Data;
using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Infrastructure
{
    public class TariffCalculationService
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly AppDbContext _context;

        public TariffCalculationService(IRepositoryWrapper repositoryWrapper, AppDbContext context)
        {
            _repositoryWrapper = repositoryWrapper;
            _context = context;
        }

        /// <summary>
        /// Calculates and creates bills for active occupancies for a given month and year.
        /// Bills are generated for the selected billing month and may be paid whenever the customer is ready.
        /// </summary>
        public void CalculateBills(int year, int month)
        {
            RemoveDuplicateBills();
            var calculatedAt = DateTime.UtcNow;
            var tariffs = GetActiveTariffs(calculatedAt);
            var firstDayOfMonth = new DateTime(year, month, 1);
            var firstDayOfNextMonth = firstDayOfMonth.AddMonths(1);

            var activeOccupancies = _repositoryWrapper.Occupancy.FindAll()
                .Where(occupancy => occupancy.StartDate < firstDayOfNextMonth
                    && (occupancy.EndDate == null || occupancy.EndDate >= firstDayOfMonth))
                .ToList();

            foreach (var occupancy in activeOccupancies)
            {
                var existingBill = _repositoryWrapper.Bill.FindAll()
                    .FirstOrDefault(bill => bill.UnitId == occupancy.UnitId && bill.Year == year && bill.Month == month);
                if (existingBill != null)
                {
                    continue;
                }

                _repositoryWrapper.Bill.Create(CreateBill(occupancy, year, month, tariffs, calculatedAt));
            }

            _repositoryWrapper.Save();
        }

        /// <summary>
        /// Recalculates parking and additional-vehicle charges for the registered customer's current bill.
        /// A bill is created when the customer has an active occupancy but the current monthly bill does not yet exist.
        /// If rent and utilities were paid before a vehicle was registered, only the new unpaid vehicle amount reopens that bill.
        /// </summary>
        public Bill? RefreshVehicleChargesForCurrentBill(string userId)
        {
            RemoveDuplicateBills();
            var calculatedAt = DateTime.UtcNow;
            // Query and update through the same DbContext used by My Bills. This avoids
            // a stale detached entity and guarantees the charge is written to Bills.
            var occupancy = _context.Occupancies
                .Where(item => item.UserId == userId
                    && item.StartDate <= calculatedAt
                    && (item.EndDate == null || item.EndDate >= calculatedAt))
                .OrderByDescending(item => item.StartDate)
                .FirstOrDefault();
            if (occupancy == null)
            {
                return null;
            }

            var year = calculatedAt.Year;
            var month = calculatedAt.Month;
            var bill = _context.Bills
                .FirstOrDefault(item => item.UnitId == occupancy.UnitId && item.Year == year && item.Month == month);
            if (bill?.Status == BillStatus.Cancelled)
            {
                return null;
            }

            var tariffs = GetActiveTariffs(calculatedAt);
            if (bill == null)
            {
                bill = CreateBill(occupancy, year, month, tariffs, calculatedAt);
                _context.Bills.Add(bill);
            }
            else
            {
                var previousVehicleCharge = bill.ParkingAmount + bill.AdditionalVehicleAmount;
                ApplyVehicleCharges(bill, occupancy.UserId, tariffs);
                var updatedVehicleCharge = bill.ParkingAmount + bill.AdditionalVehicleAmount;

                // The screenshot case: rent/utilities were already paid, then a car was
                // registered. Reopen the bill so only the new parking amount is payable.
                if (bill.Status == BillStatus.Paid && updatedVehicleCharge > previousVehicleCharge)
                {
                    bill.Status = BillStatus.Pending;
                }

                bill.UpdatedAt = calculatedAt;
            }

            _context.SaveChanges();
            return bill;
        }

        private Occupancy? FindCurrentOccupancy(string userId, DateTime asOf)
        {
            return _repositoryWrapper.Occupancy.FindAll()
                .Where(occupancy => occupancy.UserId == userId
                    && occupancy.StartDate <= asOf
                    && (occupancy.EndDate == null || occupancy.EndDate >= asOf))
                .OrderByDescending(occupancy => occupancy.StartDate)
                .FirstOrDefault();
        }

        private Bill CreateBill(Occupancy occupancy, int year, int month, TariffRates tariffs, DateTime calculatedAt)
        {
            var bill = new Bill
            {
                UnitId = occupancy.UnitId,
                Year = year,
                Month = month,
                RentAmount = tariffs.Rent,
                // Utility tariff amounts are rates per selected unit/litre, not flat charges.
                WaterAmount = 0m,
                ElectricityAmount = 0m,
                Status = BillStatus.Pending,
                // Keep the billing date for statement history; there is no restricted payment window.
                DueDate = new DateTime(year, month, 1),
                UpdatedAt = calculatedAt
            };

            ApplyVehicleCharges(bill, occupancy.UserId, tariffs);
            return bill;
        }

        private void ApplyVehicleCharges(Bill bill, string userId, TariffRates tariffs)
        {
            var vehicleCount = _repositoryWrapper.Vehicle.FindAll().Count(vehicle => vehicle.UserId == userId);
            // The first registered vehicle is free. Vehicle #2 uses the parking tariff,
            // and every vehicle after #2 uses the additional-vehicle tariff.
            var chargeableVehicles = Math.Max(0, vehicleCount - 1);
            bill.ParkingAmount = chargeableVehicles > 0 ? tariffs.Parking : 0m;
            bill.AdditionalVehicleAmount = Math.Max(0, chargeableVehicles - 1) * tariffs.AdditionalVehicle;
        }

        private void RemoveDuplicateBills()
        {
            var duplicateGroups = _context.Bills
                .Include(bill => bill.PaymentAllocations)
                .Include(bill => bill.Payments)
                .AsEnumerable()
                .GroupBy(bill => new { bill.UnitId, bill.Year, bill.Month })
                .Where(group => group.Count() > 1)
                .ToList();
            foreach (var group in duplicateGroups)
            {
                var canonical = group.OrderByDescending(bill => bill.Status == BillStatus.Paid)
                    .ThenByDescending(bill => bill.UpdatedAt).ThenByDescending(bill => bill.Id).First();
                foreach (var duplicate in group.Where(bill => bill.Id != canonical.Id))
                {
                    foreach (var allocation in duplicate.PaymentAllocations) allocation.BillId = canonical.Id;
                    foreach (var payment in duplicate.Payments) payment.BillId = canonical.Id;
                    _context.Bills.Remove(duplicate);
                }
            }
            if (duplicateGroups.Any()) _context.SaveChanges();
        }

        public decimal GetCurrentRate(TariffType type)
        {
            var now = DateTime.UtcNow;
            return _repositoryWrapper.Tariff.FindByCondition(tariff => tariff.Type == type
                && tariff.EffectiveFrom <= now
                && (tariff.EffectiveTo == null || tariff.EffectiveTo >= now))
                .Select(tariff => (decimal?)tariff.Amount).FirstOrDefault() ?? 0m;
        }
        private TariffRates GetActiveTariffs(DateTime asOf)
        {
            decimal GetTariff(TariffType type) => _repositoryWrapper.Tariff
                .FindByCondition(tariff => tariff.Type == type
                    && tariff.EffectiveFrom <= asOf
                    && (tariff.EffectiveTo == null || tariff.EffectiveTo >= asOf))
                .Select(tariff => (decimal?)tariff.Amount)
                .FirstOrDefault() ?? 0;

            return new TariffRates(
                GetTariff(TariffType.Rent),
                GetTariff(TariffType.Water),
                GetTariff(TariffType.Electricity),
                GetTariff(TariffType.Parking),
                GetTariff(TariffType.AdditionalVehicle));
        }

        private readonly record struct TariffRates(
            decimal Rent,
            decimal Water,
            decimal Electricity,
            decimal Parking,
            decimal AdditionalVehicle);
    }
}
