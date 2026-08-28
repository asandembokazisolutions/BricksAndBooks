using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Data;

namespace BricksAndBooks.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AuditController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;

        public AuditController(IRepositoryWrapper repositoryWrapper)
        {
            _repositoryWrapper = repositoryWrapper;
        }

        public IActionResult Index(string? query = null)
        {
            var logs = _repositoryWrapper.AuditLog.FindAll()
                .OrderByDescending(log => log.Timestamp)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(query))
            {
                var term = query.Trim();
                logs = logs.Where(log =>
                    (log.Action != null && log.Action.Contains(term)) ||
                    (log.EntityType != null && log.EntityType.Contains(term)) ||
                    (log.Changes != null && log.Changes.Contains(term)) ||
                    (log.UserId != null && log.UserId.Contains(term)));
            }
            return View(logs.Take(250).ToList());
        }
    }
}
