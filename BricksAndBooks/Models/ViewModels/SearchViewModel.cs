using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BricksAndBooks.Models;

namespace BricksAndBooks.Models.ViewModels
{
    public class SearchViewModel
    {
        public SearchViewModel()
        {
            SearchTerm = string.Empty;
            SearchIncludesAll = true;
            EnsureResultCollections();
        }

        [Display(Name = "Search Term")]
        public string SearchTerm { get; set; }

        [Display(Name = "Search in All")]
        public bool SearchIncludesAll { get; set; }

        [Display(Name = "Users")]
        public bool SearchIncludesUsers { get; set; }

        [Display(Name = "Bills")]
        public bool SearchIncludesBills { get; set; }

        [Display(Name = "Payments")]
        public bool SearchIncludesPayments { get; set; }

        [Display(Name = "Vehicles")]
        public bool SearchIncludesVehicles { get; set; }

        [Display(Name = "Accommodations")]
        public bool SearchIncludesAccommodations { get; set; }

        [Display(Name = "Units")]
        public bool SearchIncludesUnits { get; set; }

        public List<ApplicationUser> Users { get; set; } = new();
        public List<Bill> Bills { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();
        public List<Vehicle> Vehicles { get; set; } = new();
        public List<Accommodation> Accommodations { get; set; } = new();
        public List<Unit> Units { get; set; } = new();

        public void EnsureResultCollections()
        {
            Users ??= new List<ApplicationUser>();
            Bills ??= new List<Bill>();
            Payments ??= new List<Payment>();
            Vehicles ??= new List<Vehicle>();
            Accommodations ??= new List<Accommodation>();
            Units ??= new List<Unit>();
        }
    }
}
