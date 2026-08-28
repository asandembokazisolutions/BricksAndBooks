namespace BricksAndBooks.Models.ViewModels
{
    public class FinancialViewModel
    {
        public decimal TotalRevenue { get; set; }
        public decimal TotalPending { get; set; }
        public decimal TotalFailed { get; set; }
        public int SuccessfulPaymentCount { get; set; }
        public int PendingPaymentCount { get; set; }
        public int FailedPaymentCount { get; set; }
        public int CustomerCount { get; set; }
        public decimal OutstandingBills { get; set; }
        public List<Payment> RecentPayments { get; set; } = new();
    }
}