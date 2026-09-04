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

            string[] roleNames = { "Admin", "FinancialOfficer", "Student", "Staff" };
            foreach (var roleName in roleNames)
            {
                if (!roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
                {
                    roleManager.CreateAsync(new IdentityRole(roleName)).GetAwaiter().GetResult();
                }
            }

            SeedUser(userManager, AdministratorUserId, "admin@bricksandbooks.com", "Admin", "User", "Admin@123", "Admin", isAdmin: true, isFinancialOfficer: false);
            SeedUser(userManager, FinancialOfficerUserId, "finance@bricksandbooks.com", "Financial", "Officer", "Finance@123", "FinancialOfficer", isAdmin: false, isFinancialOfficer: true);

            if (!context.Tariffs.Any())
            {
                context.Tariffs.AddRange(
                    new Tariff { Type = TariffType.Rent, Amount = 500.00m, Description = "Monthly rent for a standard unit" },
                    new Tariff { Type = TariffType.Water, Amount = 20.00m, Description = "Monthly water charge per unit" },
                    new Tariff { Type = TariffType.Electricity, Amount = 30.00m, Description = "Monthly electricity charge per unit" },
                    new Tariff { Type = TariffType.Parking, Amount = 15.00m, Description = "Monthly parking charge per vehicle" },
                    new Tariff { Type = TariffType.AdditionalVehicle, Amount = 10.00m, Description = "Monthly charge for each additional vehicle beyond the first" });
                context.SaveChanges();
            }

            RemoveConfirmedTestBill(context);
            AlignPendingBillDueDates(context);
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
    }
}
