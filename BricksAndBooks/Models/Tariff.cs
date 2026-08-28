using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum TariffType
    {
        Rent,
        Water,
        Electricity,
        Parking,
        AdditionalVehicle
    }

    public class Tariff
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public TariffType Type { get; set; }

        [Required]
        [Range(0.01, 100000, ErrorMessage = "Amount must be between 0.01 and 100000")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; } // monthly amount unless otherwise specified

        [StringLength(200)]
        public string? Description { get; set; }

        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
        public DateTime? EffectiveTo { get; set; } // null means currently effective

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}