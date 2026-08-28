using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class OccupancyRepository : RepositoryBase<Occupancy>, IOccupancyRepository
    {
        public OccupancyRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}