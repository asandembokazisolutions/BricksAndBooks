using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BricksAndBooks.Data;
using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class RepositoryWrapper : IRepositoryWrapper
    {
        private BricksAndBooks.Data.DataAccess.AppDbContext _repoContext;

        public RepositoryWrapper(BricksAndBooks.Data.DataAccess.AppDbContext repoContext)
        {
            _repoContext = repoContext;
        }

        public IRepositoryBase<Accommodation> Accommodation => new RepositoryBase<Accommodation>(_repoContext);
        public IRepositoryBase<Unit> Unit => new RepositoryBase<Unit>(_repoContext);
        public IRepositoryBase<Tariff> Tariff => new RepositoryBase<Tariff>(_repoContext);
        public IRepositoryBase<Bill> Bill => new RepositoryBase<Bill>(_repoContext);
        public IRepositoryBase<Payment> Payment => new RepositoryBase<Payment>(_repoContext);
        public IRepositoryBase<MaintenanceRequest> MaintenanceRequest => new RepositoryBase<MaintenanceRequest>(_repoContext);
        public IRepositoryBase<ChatMessage> ChatMessage => new RepositoryBase<ChatMessage>(_repoContext);
        public IRepositoryBase<Notification> Notification => new RepositoryBase<Notification>(_repoContext);
        public IRepositoryBase<Vehicle> Vehicle => new RepositoryBase<Vehicle>(_repoContext);
        public IRepositoryBase<Occupancy> Occupancy => new RepositoryBase<Occupancy>(_repoContext);
        public IRepositoryBase<Review> Review => new RepositoryBase<Review>(_repoContext);
        public IRepositoryBase<AuditLog> AuditLog => new RepositoryBase<AuditLog>(_repoContext);
        public IRepositoryBase<PasswordResetRequest> PasswordResetRequest => new RepositoryBase<PasswordResetRequest>(_repoContext);

        public void Save()
        {
            _repoContext.SaveChanges();
        }
    }
}
