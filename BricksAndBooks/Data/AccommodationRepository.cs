using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class AccommodationRepository : RepositoryBase<Accommodation>, IAccommodationRepository
    {
        public AccommodationRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}