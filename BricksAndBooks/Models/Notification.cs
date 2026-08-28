using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum NotificationType
    {
        PaymentSuccessful,
        PaymentPending,
        PaymentFailed,
        RentDue,
        MaintenanceUpdate,
        NewMessage,
        Announcement,
        Activity
    }

    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        [Required]
        public NotificationType Type { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string Message { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }

        // Optional: link to related entity (e.g., PaymentId, MaintenanceRequestId)
        public int? RelatedEntityId { get; set; }

        // Optional: URL for deep linking in the app
        public string? Url { get; set; }
    }
}