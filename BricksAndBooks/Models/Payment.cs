using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum PaymentStatus
    {
        Pending,
        Successful,
        Failed,
        Cancelled,
        Refunded
    }

    public enum PaymentMethod
    {
        CreditCard,
        BankTransfer,
        Cash,
        MobileMoney
    }

    public class Payment
    {
        [Key]
        public int Id { get; set; }

        // Legacy link retained for older data. New payments use PaymentAllocations and can cover multiple bills.
        public int? BillId { get; set; }

        [ForeignKey(nameof(BillId))]
        public Bill? Bill { get; set; }

        [Required]
        [StringLength(100)]
        public string TransactionReferenceId { get; set; } = string.Empty; // Unique ID from payment gateway or generated

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        public PaymentMethod Method { get; set; }

        [Required]
        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        public string? GatewayResponse { get; set; } // Raw response from payment gateway (e.g., Stripe)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
    }
}