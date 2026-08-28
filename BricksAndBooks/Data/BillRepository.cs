using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class BillRepository : RepositoryBase<Bill>, IBillRepository
    {
        public BillRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}