using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum UnitOccupancyType
    {
        Single,
        Shared
    }

    public class Unit
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(10)]
        public string UnitNumber { get; set; } = string.Empty; // e.g., "101"

        [Required]
        public int AccommodationId { get; set; }

        [ForeignKey("AccommodationId")]
        public Accommodation? Accommodation { get; set; }

        [Required]
        [Range(1, 20, ErrorMessage = "Capacity must be between 1 and 20")]
        public int Capacity { get; set; } // max number of occupants

        [Required]
        public UnitOccupancyType OccupancyType { get; set; } = UnitOccupancyType.Single;

        public bool IsAvailable { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Bill> Bills { get; set; } = new List<Bill>();
        public ICollection<MaintenanceRequest> MaintenanceRequests { get; set; } = new List<MaintenanceRequest>();
    }
}