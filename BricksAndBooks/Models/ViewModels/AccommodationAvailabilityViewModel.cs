namespace BricksAndBooks.Models.ViewModels
{
    public class AccommodationAvailabilityViewModel
    {
        public int AccommodationId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int TotalUnits { get; set; }
        public int AvailableUnits { get; set; }
        public int AvailableSpaces { get; set; }
        public bool CanApply => AvailableUnits > 0 && AvailableSpaces > 0;
    }
}
