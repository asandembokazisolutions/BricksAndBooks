using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class TariffRepository : RepositoryBase<Tariff>, ITariffRepository
    {
        public TariffRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}