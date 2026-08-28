using BricksAndBooks.Models;

namespace BricksAndBooks.Models.ViewModels
{
    public class CustomerFinancialViewModel
    {
        public ApplicationUser? User { get; set; }
        public System.Collections.Generic.List<Bill> Bills { get; set; } = new System.Collections.Generic.List<Bill>();
    }
}