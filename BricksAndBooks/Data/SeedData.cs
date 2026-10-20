using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Models;
using BricksAndBooks.Data.DataAccess;

namespace BricksAndBooks.Data
{
    public static class SeedData
    {
        public const string AdministratorUserId = "00000000-0000-0000-0000-000000000001";
        public const string FinancialOfficerUserId = "00000000-0000-0000-0000-000000000002";

        public static void Initialize(IServiceProvider serviceProvider)
        {
            var context = serviceProvider.GetRequiredService<AppDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // Firestore owns durable storage. The short-lived EF compatibility cache has no SQL schema or migrations.
            context.Database.EnsureCreated();

            string[] roleNames = { "Admin", "FinancialOfficer", "Student", "Staff", "MaintenanceWorker" };
            foreach (var roleName in roleNames)
            {
                if (!roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
                {
                    roleManager.CreateAsync(new IdentityRole(roleName)).GetAwaiter().GetResult();
                }
            }

            SeedUser(userManager, AdministratorUserId, "admin@bricksandbooks.com", "Admin", "User", "Admin@123", "Admin", isAdmin: true, isFinancialOfficer: false);
            SeedUser(userManager, FinancialOfficerUserId, "finance@bricksandbooks.com", "Financial", "Officer", "Finance@123", "FinancialOfficer", isAdmin: false, isFinancialOfficer: true);
            SeedMaintenanceSpecialists(userManager);

            var existingWaterRate = context.Tariffs.FirstOrDefault(tariff => tariff.Type == TariffType.Water && tariff.EffectiveTo == null);
            var existingElectricityRate = context.Tariffs.FirstOrDefault(tariff => tariff.Type == TariffType.Electricity && tariff.EffectiveTo == null);
            var existingParkingRate = context.Tariffs.FirstOrDefault(tariff => tariff.Type == TariffType.Parking && tariff.EffectiveTo == null);
            var existingAdditionalVehicleRate = context.Tariffs.FirstOrDefault(tariff => tariff.Type == TariffType.AdditionalVehicle && tariff.EffectiveTo == null);
            if (existingWaterRate != null) { existingWaterRate.Amount = 0.30m; existingWaterRate.Description = "Water rate per litre"; }
            if (existingElectricityRate != null) { existingElectricityRate.Amount = 0.47m; existingElectricityRate.Description = "Electricity rate per kWh"; }
            if (existingParkingRate != null) { existingParkingRate.Amount = 100.00m; existingParkingRate.Description = "Monthly charge for the second vehicle; the first vehicle is free"; }
            if (existingAdditionalVehicleRate != null) { existingAdditionalVehicleRate.Amount = 100.00m; existingAdditionalVehicleRate.Description = "Monthly charge for each vehicle after the second"; }
            if (existingWaterRate == null) context.Tariffs.Add(new Tariff { Type = TariffType.Water, Amount = 0.30m, Description = "Water rate per litre" });
            if (existingElectricityRate == null) context.Tariffs.Add(new Tariff { Type = TariffType.Electricity, Amount = 0.47m, Description = "Electricity rate per kWh" });
            if (existingParkingRate == null) context.Tariffs.Add(new Tariff { Type = TariffType.Parking, Amount = 100.00m, Description = "Monthly charge for the second vehicle; the first vehicle is free" });
            if (existingAdditionalVehicleRate == null) context.Tariffs.Add(new Tariff { Type = TariffType.AdditionalVehicle, Amount = 100.00m, Description = "Monthly charge for each vehicle after the second" });
            if (!context.Tariffs.Any(tariff => tariff.Type == TariffType.Rent))
            {
                context.Tariffs.Add(new Tariff { Type = TariffType.Rent, Amount = 500.00m, Description = "Monthly rent for a standard unit" });
            }
            if (existingWaterRate != null || existingElectricityRate != null || existingParkingRate != null || existingAdditionalVehicleRate != null || context.ChangeTracker.HasChanges()) context.SaveChanges();

            RemoveConfirmedTestBill(context);
            RemoveConfirmedTestPayment(context);
            AlignPendingBillDueDates(context);
        }

        private static void RemoveConfirmedTestPayment(AppDbContext context)
        {
            // Remove only the payment explicitly identified by the project owner. Bills and
            // all unrelated payment records remain untouched.
            const string testReference = "PAY-90F7B64F1E154864908EE7961AFDC3E8";
            var payment = context.Payments
                .Include(item => item.Allocations)
                .SingleOrDefault(item => item.TransactionReferenceId == testReference);
            if (payment == null)
            {
                return;
            }

            context.PaymentAllocations.RemoveRange(payment.Allocations);
            context.Payments.Remove(payment);
            context.SaveChanges();
        }

        private static void RemoveConfirmedTestBill(AppDbContext context)
        {
            // Remove only the confirmed August 2026 R565 test bill. This is intentionally
            // scoped by the exact billing period and charge breakdown so normal bills and
            // tariff configuration are never removed.
            var testBills = context.Bills
                .Include(bill => bill.PaymentAllocations)
                .Include(bill => bill.Payments)
                .Where(bill => bill.Year == 2026
                    && bill.Month == 8
                    && bill.RentAmount == 500m
                    && bill.WaterAmount == 20m
                    && bill.ElectricityAmount == 30m
                    && bill.ParkingAmount == 15m
                    && bill.AdditionalVehicleAmount == 0m)
                .ToList();

            if (!testBills.Any())
            {
                return;
            }

            var testBillIds = testBills.Select(bill => bill.Id).ToHashSet();
            var allocations = context.PaymentAllocations
                .Where(allocation => testBillIds.Contains(allocation.BillId))
                .ToList();
            var paymentIds = allocations.Select(allocation => allocation.PaymentId).ToHashSet();
            var payments = context.Payments
                .Include(payment => payment.Allocations)
                .Where(payment => paymentIds.Contains(payment.Id)
                    && payment.Allocations.All(allocation => testBillIds.Contains(allocation.BillId)))
                .ToList();

            context.PaymentAllocations.RemoveRange(allocations);
            context.Payments.RemoveRange(payments);
            context.Bills.RemoveRange(testBills);
            context.SaveChanges();
        }

        private static void AlignPendingBillDueDates(AppDbContext context)
        {
            var now = DateTime.UtcNow;
            var pendingBills = context.Bills
                .Where(bill => bill.Status == BillStatus.Pending)
                .ToList();

            var changed = false;
            foreach (var bill in pendingBills)
            {
                var monthlyDeadline = new DateTime(bill.Year, bill.Month, 1);
                if (bill.DueDate.Date == monthlyDeadline.Date)
                {
                    continue;
                }

                bill.DueDate = monthlyDeadline;
                bill.UpdatedAt = now;
                changed = true;
            }

            if (changed)
            {
                context.SaveChanges();
            }
        }

        private static void SeedUser(UserManager<ApplicationUser> userManager, string id, string email, string firstName, string lastName, string password, string role, bool isAdmin, bool isFinancialOfficer)
        {
            var user = userManager.FindByEmailAsync(email).GetAwaiter().GetResult();
            if (user == null)
            {
                user = new ApplicationUser
                {
                    Id = id,
                    UserName = email,
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    IsAdmin = isAdmin,
                    IsFinancialOfficer = isFinancialOfficer,
                    EmailConfirmed = true
                };
                var result = userManager.CreateAsync(user, password).GetAwaiter().GetResult();
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Unable to seed {role} account: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }

            // Keep the profile flags aligned for both newly seeded and existing accounts.
            if (user.IsAdmin != isAdmin || user.IsFinancialOfficer != isFinancialOfficer)
            {
                user.IsAdmin = isAdmin;
                user.IsFinancialOfficer = isFinancialOfficer;
                userManager.UpdateAsync(user).GetAwaiter().GetResult();
            }

            if (!userManager.IsInRoleAsync(user, role).GetAwaiter().GetResult())
            {
                userManager.AddToRoleAsync(user, role).GetAwaiter().GetResult();
            }
        }

        private static void SeedMaintenanceSpecialists(UserManager<ApplicationUser> userManager)
        {
            var specialists = new[]
            {
                new { Id = "00000000-0000-0000-0000-000000000101", Email = "electrical@bricksandbooks.com", FirstName = "Lebo", LastName = "Mokoena", Password = "Electrical@123", Specialty = MaintenanceCategory.Electrical },
                new { Id = "00000000-0000-0000-0000-000000000102", Email = "plumbing@bricksandbooks.com", FirstName = "Thabo", LastName = "Ndlovu", Password = "Plumbing@123", Specialty = MaintenanceCategory.Plumbing },
                new { Id = "00000000-0000-0000-0000-000000000103", Email = "hvac@bricksandbooks.com", FirstName = "Mpho", LastName = "Dlamini", Password = "Hvac@123", Specialty = MaintenanceCategory.HVAC },
                new { Id = "00000000-0000-0000-0000-000000000104", Email = "structural@bricksandbooks.com", FirstName = "Anele", LastName = "Jacobs", Password = "Structural@123", Specialty = MaintenanceCategory.Structural },
                new { Id = "00000000-0000-0000-0000-000000000105", Email = "appliances@bricksandbooks.com", FirstName = "Zinhle", LastName = "Khumalo", Password = "Appliance@123", Specialty = MaintenanceCategory.Appliance },
                new { Id = "00000000-0000-0000-0000-000000000106", Email = "general-maintenance@bricksandbooks.com", FirstName = "Siyabonga", LastName = "Maseko", Password = "General@123", Specialty = MaintenanceCategory.Other }
            };

            foreach (var specialist in specialists)
            {
                var user = userManager.FindByEmailAsync(specialist.Email).GetAwaiter().GetResult();
                if (user == null)
                {
                    user = new ApplicationUser
                    {
                        Id = specialist.Id,
                        UserName = specialist.Email,
                        Email = specialist.Email,
                        FirstName = specialist.FirstName,
                        LastName = specialist.LastName,
                        IsMaintenanceWorker = true,
                        MaintenanceSpecialty = specialist.Specialty,
                        EmailConfirmed = true
                    };
                    var result = userManager.CreateAsync(user, specialist.Password).GetAwaiter().GetResult();
                    if (!result.Succeeded)
                    {
                        throw new InvalidOperationException($"Unable to seed {specialist.Specialty} maintenance specialist: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                    }
                }

                if (!user.IsMaintenanceWorker || user.MaintenanceSpecialty != specialist.Specialty)
                {
                    user.IsMaintenanceWorker = true;
                    user.MaintenanceSpecialty = specialist.Specialty;
                    userManager.UpdateAsync(user).GetAwaiter().GetResult();
                }

                if (!userManager.IsInRoleAsync(user, "MaintenanceWorker").GetAwaiter().GetResult())
                {
                    userManager.AddToRoleAsync(user, "MaintenanceWorker").GetAwaiter().GetResult();
                }
            }
        }
    }
}
