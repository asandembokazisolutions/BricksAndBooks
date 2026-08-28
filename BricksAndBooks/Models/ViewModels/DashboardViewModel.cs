namespace BricksAndBooks.Models.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalAccommodations { get; set; }
        public int TotalUnits { get; set; }
        public int TotalBills { get; set; }
        public int TotalPayments { get; set; }
        public int PendingPayments { get; set; }
        public int TotalUsers { get; set; }
        public int TotalStudents { get; set; }
        public int TotalStaff { get; set; }
        public int TotalFinancialOfficers { get; set; }
        public int OccupiedUnits { get; set; }
        public int AvailableUnits { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal OutstandingBalance { get; set; }

        // Public landing-page residences, including their units and uploaded image paths.
        public List<Accommodation> FeaturedAccommodations { get; set; } = new();
    }
}