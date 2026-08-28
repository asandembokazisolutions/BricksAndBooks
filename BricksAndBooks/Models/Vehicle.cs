using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public class Vehicle
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [Required]
        [StringLength(20)]
        public string RegistrationNumber { get; set; }

        [Required]
        [StringLength(50)]
        public string Make { get; set; }

        [Required]
        [StringLength(50)]
        public string Model { get; set; }

        [Required]
        [StringLength(30)]
        public string Colour { get; set; }

        [Required]
        [StringLength(50)]
        public string VehicleType { get; set; } // e.g., Sedan, SUV, Motorcycle

        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Is this vehicle's parking paid for? Or we can compute from bills.
        // We'll leave it to the billing system to determine parking charges based on the number of vehicles.
    }
}