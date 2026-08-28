using System.Security.Claims;
using BricksAndBooks.Data;
using BricksAndBooks.Models;

namespace BricksAndBooks.Infrastructure
{
    public class AuditService
    {
        private readonly IRepositoryWrapper _repositoryWrapper;

        public AuditService(IRepositoryWrapper repositoryWrapper)
        {
            _repositoryWrapper = repositoryWrapper;
        }

        public void Record(ClaimsPrincipal actor, string action, string entityType, int entityId, string changes)
        {
            var userId = actor.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = actor.IsInRole("Admin") ? "Admin" : actor.IsInRole("FinancialOfficer") ? "FinancialOfficer" : actor.IsInRole("Staff") ? "Staff" : actor.IsInRole("Student") ? "Student" : "Unknown";
            _repositoryWrapper.AuditLog.Create(new AuditLog
            {
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Changes = $"Role: {role}. {changes}",
                Timestamp = DateTime.UtcNow
            });
            _repositoryWrapper.Save();
        }
    }
}
