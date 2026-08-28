using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class NotificationRepository : RepositoryBase<Notification>, INotificationRepository
    {
        public NotificationRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}