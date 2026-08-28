using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using BricksAndBooks.Data;
using BricksAndBooks.Models;

namespace BricksAndBooks.Controllers
{
    [Authorize] // Any authenticated user can view their notifications
    public class NotificationController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationController(IRepositoryWrapper repositoryWrapper, UserManager<ApplicationUser> userManager)
        {
            _repositoryWrapper = repositoryWrapper;
            _userManager = userManager;
        }

        // GET: Notification
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var notifications = _repositoryWrapper.Notification.FindAll()
                .Where(n => n.UserId == user.Id)
                .OrderByDescending(n => n.CreatedAt)
                .ToList();

            return View(notifications);
        }

        // GET: Notification/Unread — lightweight polling source for the in-app notification pop-up.
        [HttpGet]
        public async Task<IActionResult> Unread()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            var notifications = _repositoryWrapper.Notification.FindByCondition(n => n.UserId == user.Id && !n.IsRead)
                .OrderByDescending(n => n.CreatedAt)
                .Take(10)
                .Select(n => new
                {
                    n.Id,
                    n.Title,
                    n.Message,
                    Type = n.Type.ToString(),
                    n.Url,
                    n.CreatedAt
                })
                .ToList();

            return Json(notifications);
        }

        // GET: Notification/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var notificationQuery = _repositoryWrapper.Notification.FindByCondition(n => n.Id == id);
            var notification = notificationQuery.FirstOrDefault();
            var user = await _userManager.GetUserAsync(User);
            if (notification == null)
            {
                return NotFound();
            }
            if (user == null || notification.UserId != user.Id)
            {
                return Forbid();
            }

            return View(notification);
        }

        // POST: Notification/MarkAsRead/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var notificationQuery = _repositoryWrapper.Notification.FindByCondition(n => n.Id == id);
            var notification = notificationQuery.FirstOrDefault();
            var user = await _userManager.GetUserAsync(User);
            if (notification == null)
            {
                return NotFound();
            }
            if (user == null || notification.UserId != user.Id)
            {
                return Forbid();
            }

            if (ModelState.IsValid)
            {
                notification.IsRead = true;
                notification.ReadAt = System.DateTime.UtcNow;
                _repositoryWrapper.Notification.Update(notification);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index));
            }
            return View(notification);
        }

        // We might want to have a method to mark all as read, but we'll leave it for now.
    }
}