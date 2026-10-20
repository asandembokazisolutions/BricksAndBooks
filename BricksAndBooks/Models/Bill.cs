using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum BillStatus
    {
        Pending,
        Paid,
        Failed,
        Cancelled
    }

    public class Bill
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UnitId { get; set; }

        [ForeignKey("UnitId")]
        public Unit Unit { get; set; }

        [Required]
        public int Year { get; set; } // e.g., 2026

        [Required]
        [Range(1, 12)]
        public int Month { get; set; } // 1-12

        [Column(TypeName = "decimal(18,2)")]
        public decimal RentAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal WaterAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ElectricityAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ParkingAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AdditionalVehicleAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount
        {
            get => RentAmount + WaterAmount + ElectricityAmount + ParkingAmount + AdditionalVehicleAmount;
        }

        public BillStatus Status { get; set; } = BillStatus.Pending;

        // The billing date is retained for statement history; payment can be made at any time.
        public DateTime DueDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();
    }
}
