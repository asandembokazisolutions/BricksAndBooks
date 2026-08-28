using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public class Accommodation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "Townhouse Complex A"

        [Required]
        [StringLength(200)]
        public string Address { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        // Pipe-delimited relative paths for uploaded gallery images.
        [StringLength(4000)]
        public string? ImageUrls { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public ICollection<Unit> Units { get; set; } = new List<Unit>();
    }
}