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

        // Bills belong to a resident, not only to a physical unit. This prevents a new
        // occupant of a shared unit from inheriting another occupant's paid bill.
        [Required]
        public string UserId { get; set; } = string.Empty;

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

        // The final day of the monthly payment window. Payment is due by this date.
        public DateTime DueDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The payment window opens on the 30th of the preceding month and closes on the first day of this bill's month.
        /// For shorter months, it opens on that previous month's final available day.
        /// </summary>
        [NotMapped]
        public DateTime PaymentWindowStart
        {
            get
            {
                var previousMonth = new DateTime(Year, Month, 1).AddMonths(-1);
                var startDay = Math.Min(30, DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month));
                return new DateTime(previousMonth.Year, previousMonth.Month, startDay);
            }
        }

        /// <summary>
        /// A bill is overdue from the second day of its bill month when it has not been paid or cancelled.
        /// </summary>
        [NotMapped]
        public bool IsOverdue => Status != BillStatus.Paid
            && Status != BillStatus.Cancelled
            && DateTime.UtcNow.Date > DueDate.Date;

        // Navigation properties
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<PaymentAllocation> PaymentAllocations { get; set; } = new List<PaymentAllocation>();
    }
}
