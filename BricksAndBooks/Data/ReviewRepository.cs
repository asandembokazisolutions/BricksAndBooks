using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class ReviewRepository : RepositoryBase<Review>, IReviewRepository
    {
        public ReviewRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}