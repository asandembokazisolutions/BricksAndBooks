using System.ComponentModel.DataAnnotations;
using BricksAndBooks.Models;

namespace BricksAndBooks.Models.ViewModels
{
    public class FlexiblePaymentViewModel
    {
        [Required]
        [Display(Name = "Payment method")]
        public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;

        // Card values are accepted only for the current request and are never persisted.
        [Display(Name = "Cardholder name")]
        public string? CardholderName { get; set; }

        [Display(Name = "Card number")]
        public string? CardNumber { get; set; }

        [Display(Name = "Expiry date")]
        public string? CardExpiry { get; set; }

        [Display(Name = "CVV")]
        public string? CardCvv { get; set; }

        public List<FlexiblePaymentItemViewModel> Items { get; set; } = new List<FlexiblePaymentItemViewModel>();

        public decimal SelectedTotal => Items.Where(item => item.IsSelected).Sum(item => item.Amount);
    }

    public class FlexiblePaymentItemViewModel
    {
        public int BillId { get; set; }
        public BillChargeType ChargeType { get; set; }
        public string ChargeLabel { get; set; } = string.Empty;
        public string BillingPeriod { get; set; } = string.Empty;
        public DateTime DueDate { get; set; }
        public decimal Amount { get; set; }
        public decimal AlreadyAllocated { get; set; }
        public bool IsSelected { get; set; }

        public decimal OutstandingAmount => Math.Max(0, Amount - AlreadyAllocated);
    }
}
