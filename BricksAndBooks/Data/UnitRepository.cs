using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class UnitRepository : RepositoryBase<Unit>, IUnitRepository
    {
        public UnitRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}