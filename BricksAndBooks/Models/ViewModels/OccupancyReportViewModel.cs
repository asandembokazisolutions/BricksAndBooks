namespace BricksAndBooks.Models.ViewModels
{
    public class OccupancyReportViewModel
    {
        public string? AccommodationName { get; set; }
        public int TotalUnits { get; set; }
        public int OccupiedUnits { get; set; }
        public double OccupancyRate { get; set; }
    }
}