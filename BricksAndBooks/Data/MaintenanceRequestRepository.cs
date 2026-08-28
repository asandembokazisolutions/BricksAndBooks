using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class MaintenanceRequestRepository : RepositoryBase<MaintenanceRequest>, IMaintenanceRequestRepository
    {
        public MaintenanceRequestRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}