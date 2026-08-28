using BricksAndBooks.Models;

namespace BricksAndBooks.Models.ViewModels
{
    public class PaymentReceiptViewModel
    {
        public Payment Payment { get; set; } = null!;
        public List<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
        public Unit? Unit { get; set; }
        public Accommodation? Accommodation { get; set; }
        public ApplicationUser? Customer { get; set; }
        public string CustomerIdentifier => Customer?.StudentNumber ?? Customer?.EmployeeNumber ?? "Not recorded";
    }
}
