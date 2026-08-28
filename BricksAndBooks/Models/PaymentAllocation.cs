using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum BillChargeType
    {
        Rent,
        Water,
        Electricity,
        Parking,
        AdditionalVehicle
    }

    public class PaymentAllocation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PaymentId { get; set; }

        [ForeignKey(nameof(PaymentId))]
        public Payment Payment { get; set; } = null!;

        [Required]
        public int BillId { get; set; }

        [ForeignKey(nameof(BillId))]
        public Bill Bill { get; set; } = null!;

        [Required]
        public BillChargeType ChargeType { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
