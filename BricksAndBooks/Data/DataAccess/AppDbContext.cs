using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Models;

namespace BricksAndBooks.Data.DataAccess
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        // DbSets for our entities
        public DbSet<Accommodation> Accommodations { get; set; }
        public DbSet<Unit> Units { get; set; }
        public DbSet<Tariff> Tariffs { get; set; }
        public DbSet<Bill> Bills { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<PaymentAllocation> PaymentAllocations { get; set; }
        public DbSet<MaintenanceRequest> MaintenanceRequests { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Vehicle> Vehicles { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Occupancy> Occupancies { get; set; }
        public DbSet<AccommodationApplication> AccommodationApplications { get; set; }
        public DbSet<PasswordResetRequest> PasswordResetRequests { get; set; }
        public DbSet<Announcement> Announcements { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            // Customize the ASP.NET Identity model and override the defaults if needed.
            // For example, you can rename the ASP.NET Identity table names and more.
            // Add your custom configurations after calling base.OnModelCreating(builder);

            // Configure Accommodation-Unit relationship (one-to-many)
            builder.Entity<Unit>()
                .HasOne(u => u.Accommodation)
                .WithMany(a => a.Units)
                .HasForeignKey(u => u.AccommodationId)
                .OnDelete(DeleteBehavior.Cascade); // Delete units when accommodation is deleted

            // Configure Occupancy-User relationship (one-to-many, restrict delete)
            builder.Entity<Occupancy>()
                .HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure Occupancy-Unit relationship (one-to-many, restrict delete)
            builder.Entity<Occupancy>()
                .HasOne(o => o.Unit)
                .WithMany()
                .HasForeignKey(o => o.UnitId)
                .OnDelete(DeleteBehavior.Restrict);

            // Applications remain as an audit trail and cannot be deleted when related users, accommodations, or units are removed.
            builder.Entity<AccommodationApplication>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AccommodationApplication>()
                .HasOne(a => a.Accommodation)
                .WithMany()
                .HasForeignKey(a => a.AccommodationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AccommodationApplication>()
                .HasOne(a => a.AssignedUnit)
                .WithMany()
                .HasForeignKey(a => a.AssignedUnitId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<AccommodationApplication>()
                .HasIndex(a => new { a.UserId, a.Status });

            // Reset requests remain as an audit trail; keep them even if the requesting or
            // resolving user account is later removed.
            builder.Entity<PasswordResetRequest>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<PasswordResetRequest>()
                .HasOne(r => r.ResolvedByUser)
                .WithMany()
                .HasForeignKey(r => r.ResolvedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<PasswordResetRequest>()
                .HasIndex(r => r.Status);

            builder.Entity<Announcement>()
                .HasIndex(a => new { a.IsPublished, a.PublishedAt });

            builder.Entity<AccommodationApplication>()
                .HasIndex(a => new { a.AccommodationId, a.Status });

            // Configure Unit-Bill relationship (one-to-many)
            builder.Entity<Bill>()
                .HasOne(b => b.Unit)
                .WithMany(u => u.Bills)
                .HasForeignKey(b => b.UnitId)
                .OnDelete(DeleteBehavior.Cascade); // Delete bills when unit is deleted

            builder.Entity<Bill>()
                .HasIndex(b => new { b.UnitId, b.Year, b.Month })
                .IsUnique();

            // Legacy optional Payment-Bill relationship. New payments can cover multiple bills through allocations.
            builder.Entity<Payment>()
                .HasOne(p => p.Bill)
                .WithMany(b => b.Payments)
                .HasForeignKey(p => p.BillId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<PaymentAllocation>()
                .HasOne(allocation => allocation.Payment)
                .WithMany(payment => payment.Allocations)
                .HasForeignKey(allocation => allocation.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PaymentAllocation>()
                .HasOne(allocation => allocation.Bill)
                .WithMany(bill => bill.PaymentAllocations)
                .HasForeignKey(allocation => allocation.BillId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure MaintenanceRequest-User relationship (one-to-many, restrict delete)
            builder.Entity<MaintenanceRequest>()
                .HasOne(mr => mr.User)
                .WithMany()
                .HasForeignKey(mr => mr.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Keep maintenance requests when user is deleted

            // Configure MaintenanceRequest-Unit relationship (one-to-many, restrict delete)
            builder.Entity<MaintenanceRequest>()
                .HasOne(mr => mr.Unit)
                .WithMany()
                .HasForeignKey(mr => mr.UnitId)
                .OnDelete(DeleteBehavior.Restrict); // Keep maintenance requests when unit is deleted

            // Configure MaintenanceRequest-AssignedToUser relationship (one-to-many, nullable, set null on delete)
            builder.Entity<MaintenanceRequest>()
                .HasOne(mr => mr.AssignedToUser)
                .WithMany()
                .HasForeignKey(mr => mr.AssignedToUserId)
                .OnDelete(DeleteBehavior.SetNull); // Set assigned to null when user is deleted

            // Configure ChatMessage-Sender relationship (one-to-many, restrict delete)
            builder.Entity<ChatMessage>()
                .HasOne(cm => cm.Sender)
                .WithMany()
                .HasForeignKey(cm => cm.SenderUserId)
                .OnDelete(DeleteBehavior.Restrict); // Keep chat messages when sender is deleted

            // Configure ChatMessage-Recipient relationship (one-to-many, restrict delete)
            builder.Entity<ChatMessage>()
                .HasOne(cm => cm.Recipient)
                .WithMany()
                .HasForeignKey(cm => cm.RecipientUserId)
                .OnDelete(DeleteBehavior.Restrict); // Keep chat messages when recipient is deleted

            // Configure Notification-User relationship (one-to-many, restrict delete)
            builder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Keep notifications when user is deleted

            // Configure Vehicle-User relationship (one-to-many, restrict delete)
            builder.Entity<Vehicle>()
                .HasOne(v => v.User)
                .WithMany()
                .HasForeignKey(v => v.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Keep vehicles when user is deleted

            // Configure Review-User relationship (one-to-many, restrict delete)
            builder.Entity<Review>()
                .HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict); // Keep reviews when user is deleted

            // Configure Review-Accommodation relationship (one-to-many, cascade delete? If accommodation is deleted, reviews should be deleted)
            builder.Entity<Review>()
                .HasOne(r => r.Accommodation)
                .WithMany()
                .HasForeignKey(r => r.AccommodationId)
                .OnDelete(DeleteBehavior.Cascade); // Delete reviews when accommodation is deleted

            // Configure unique index for TransactionReferenceId in Payment table
            builder.Entity<Payment>()
                .HasIndex(p => p.TransactionReferenceId)
                .IsUnique();

            // Configure composite unique index for UnitNumber + AccommodationId in Unit table
            builder.Entity<Unit>()
                .HasIndex(u => new { u.UnitNumber, u.AccommodationId })
                .IsUnique();

            // Configure composite unique index for Year + Month + UnitId in Bill table (one bill per unit per month)
            builder.Entity<Bill>()
                .HasIndex(b => new { b.Year, b.Month, b.UnitId })
                .IsUnique();

            // Add indexes for foreign keys (non-unique) to improve query performance
            builder.Entity<Unit>()
                .HasIndex(u => u.AccommodationId);

            builder.Entity<Bill>()
                .HasIndex(b => b.UnitId);

            builder.Entity<Occupancy>()
                .HasIndex(o => o.UserId);

            builder.Entity<Occupancy>()
                .HasIndex(o => o.UnitId);

            builder.Entity<Occupancy>()
                .HasIndex(o => new { o.UserId, o.UnitId, o.EndDate });

            builder.Entity<Payment>()
                .HasIndex(p => p.BillId);

            builder.Entity<PaymentAllocation>()
                .HasIndex(allocation => allocation.PaymentId);

            builder.Entity<PaymentAllocation>()
                .HasIndex(allocation => allocation.BillId);

            builder.Entity<PaymentAllocation>()
                .HasIndex(allocation => new { allocation.PaymentId, allocation.BillId, allocation.ChargeType })
                .IsUnique();

            builder.Entity<MaintenanceRequest>()
                .HasIndex(mr => mr.UserId);

            builder.Entity<MaintenanceRequest>()
                .HasIndex(mr => mr.UnitId);

            builder.Entity<MaintenanceRequest>()
                .HasIndex(mr => mr.AssignedToUserId);

            builder.Entity<ChatMessage>()
                .HasIndex(cm => cm.SenderUserId);

            builder.Entity<ChatMessage>()
                .HasIndex(cm => cm.RecipientUserId);

            builder.Entity<Notification>()
                .HasIndex(n => n.UserId);

            builder.Entity<Vehicle>()
                .HasIndex(v => v.UserId);

            builder.Entity<AuditLog>()
                .HasIndex(a => new { a.UserId, a.Timestamp });

            builder.Entity<AuditLog>()
                .HasIndex(a => new { a.EntityType, a.EntityId });

            builder.Entity<Review>()
                .HasIndex(r => r.UserId);

            builder.Entity<Review>()
                .HasIndex(r => r.AccommodationId);

            // Configure precision for decimal properties
            builder.Entity<Bill>()
                .Property(b => b.RentAmount)
                .HasPrecision(18, 2);

            builder.Entity<Bill>()
                .Property(b => b.WaterAmount)
                .HasPrecision(18, 2);

            builder.Entity<Bill>()
                .Property(b => b.ElectricityAmount)
                .HasPrecision(18, 2);

            builder.Entity<Bill>()
                .Property(b => b.ParkingAmount)
                .HasPrecision(18, 2);

            builder.Entity<Bill>()
                .Property(b => b.AdditionalVehicleAmount)
                .HasPrecision(18, 2);

            builder.Entity<Payment>()
                .Property(p => p.Amount)
                .HasPrecision(18, 2);

            builder.Entity<PaymentAllocation>()
                .Property(allocation => allocation.Amount)
                .HasPrecision(18, 2);

            builder.Entity<Tariff>()
                .Property(t => t.Amount)
                .HasPrecision(18, 2);
        }
    }
}
