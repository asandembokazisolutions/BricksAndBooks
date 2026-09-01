using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BricksAndBooks.Data;
using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public interface IRepositoryWrapper
    {
        IRepositoryBase<Accommodation> Accommodation { get; }
        IRepositoryBase<Unit> Unit { get; }
        IRepositoryBase<Tariff> Tariff { get; }
        IRepositoryBase<Bill> Bill { get; }
        IRepositoryBase<Payment> Payment { get; }
        IRepositoryBase<MaintenanceRequest> MaintenanceRequest { get; }
        IRepositoryBase<ChatMessage> ChatMessage { get; }
        IRepositoryBase<Notification> Notification { get; }
        IRepositoryBase<Vehicle> Vehicle { get; }
        IRepositoryBase<Occupancy> Occupancy { get; }
        IRepositoryBase<Review> Review { get; }
        IRepositoryBase<AuditLog> AuditLog { get; }
        IRepositoryBase<PasswordResetRequest> PasswordResetRequest { get; }

        void Save();
    }
}
