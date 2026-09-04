using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public class Occupancy
    {
        [Key] public int Id { get; set; }
        [Required] public string UserId { get; set; } = string.Empty;
        [ForeignKey("UserId")] public ApplicationUser? User { get; set; }
        [Required] public int UnitId { get; set; }
        [ForeignKey("UnitId")] public Unit? Unit { get; set; }
        [Required, StringLength(30)] public string BedNumber { get; set; } = "Bed 1";
        [Required] public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
