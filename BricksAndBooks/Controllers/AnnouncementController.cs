using BricksAndBooks.Data.DataAccess;
using BricksAndBooks.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BricksAndBooks.Controllers
{
    [Authorize]
    public class AnnouncementController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        public AnnouncementController(AppDbContext context, UserManager<ApplicationUser> userManager) { _context = context; _userManager = userManager; }

        public async Task<IActionResult> Index() => View(await _context.Announcements
            .Where(a => a.IsPublished && (a.ExpiresAt == null || a.ExpiresAt >= DateTime.UtcNow))
            .OrderByDescending(a => a.PublishedAt).ToListAsync());

        [Authorize(Roles = "Admin")]
        public IActionResult Create() => View(new Announcement());

        [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(Announcement model)
        {
            if (!ModelState.IsValid) return View(model);
            model.CreatedByUserId = _userManager.GetUserId(User);
            model.PublishedAt = DateTime.UtcNow;
            _context.Announcements.Add(model);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
