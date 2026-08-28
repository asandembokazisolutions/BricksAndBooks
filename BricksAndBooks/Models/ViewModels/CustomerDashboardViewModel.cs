using BricksAndBooks.Models;

namespace BricksAndBooks.Models.ViewModels
{
    public class CustomerDashboardViewModel
    {
        public ApplicationUser? User { get; set; }
        public Accommodation? Accommodation { get; set; }
        public Unit? Unit { get; set; }
        public AccommodationApplication? LatestApplication { get; set; }
        public decimal OutstandingBalance { get; set; }
        public decimal RentDue { get; set; }
        public decimal WaterDue { get; set; }
        public decimal ElectricityDue { get; set; }
        public decimal ParkingDue { get; set; }
        public int UnreadNotifications { get; set; }
        public int UnreadMessages { get; set; }
        public List<Bill> RecentBills { get; set; } = new();
        public List<Payment> RecentPayments { get; set; } = new();
    }
}
