using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum AccommodationApplicationStatus
    {
        Pending,
        Approved,
        Denied,
        TerminationRequested,
        Terminated
    }

    public class AccommodationApplication
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        [Required]
        public int AccommodationId { get; set; }

        [ForeignKey(nameof(AccommodationId))]
        public Accommodation? Accommodation { get; set; }

        public int? AssignedUnitId { get; set; }

        [ForeignKey(nameof(AssignedUnitId))]
        public Unit? AssignedUnit { get; set; }

        [MaxLength(2000)]
        public string? ApplicantMessage { get; set; }

        [MaxLength(2000)]
        public string? AdminNotes { get; set; }

        public AccommodationApplicationStatus Status { get; set; } = AccommodationApplicationStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReviewedAt { get; set; }
        public DateTime? TerminationRequestedAt { get; set; }
        public DateTime? TerminatedAt { get; set; }
    }
}
