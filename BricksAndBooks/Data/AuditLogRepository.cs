using BricksAndBooks.Models;
using BricksAndBooks.Data;

namespace BricksAndBooks.Data
{
    public class AuditLogRepository : RepositoryBase<AuditLog>, IAuditLogRepository
    {
        public AuditLogRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}