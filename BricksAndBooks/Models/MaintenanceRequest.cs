using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum MaintenanceStatus
    {
        Submitted,
        UnderReview,
        Assigned,
        InProgress,
        Resolved,
        Closed
    }

    public enum MaintenanceCategory
    {
        Electrical,
        Plumbing,
        HVAC,
        Structural,
        Appliance,
        Other
    }

    public class MaintenanceRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } // The user who reported the issue (tenant)

        [ForeignKey("UserId")]
        public ApplicationUser User { get; set; }

        [Required]
        public int UnitId { get; set; } // The unit where the issue is

        [ForeignKey("UnitId")]
        public Unit Unit { get; set; }

        [Required]
        public MaintenanceCategory Category { get; set; }

        [Required]
        [StringLength(500)]
        public string Description { get; set; }

        // For attachments, we can store a comma-separated list of file paths or URLs, or create a separate table.
        // For simplicity, we'll store a list of URLs as a string.
        [StringLength(2000)]
        public string? AttachmentUrls { get; set; }

        [Required]
        public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Submitted;

        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Optional: assigned to a staff member or financial officer
        public string? AssignedToUserId { get; set; }

        [ForeignKey("AssignedToUserId")]
        public ApplicationUser? AssignedToUser { get; set; }

        public DateTime? AssignedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
    }
}