using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BricksAndBooks.Models
{
    public enum PaymentStatus { Pending, Successful, Failed, Cancelled, Refunded }
    public enum PaymentMethod { CreditCard, BankTransfer, Cash, MobileMoney }

    public class Payment
    {
        [Key] public int Id { get; set; }
        public int? BillId { get; set; }
        [ForeignKey(nameof(BillId))] public Bill? Bill { get; set; }
        [Required, StringLength(100)] public string TransactionReferenceId { get; set; } = string.Empty;
        [Required, Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
        [Required] public PaymentMethod Method { get; set; }
        [Required] public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
        [StringLength(2000)] public string? GatewayResponse { get; set; }
        [StringLength(1000)] public string? ProofOfPaymentUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
    }
}
