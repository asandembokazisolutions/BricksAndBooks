using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Data;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models;

namespace BricksAndBooks.Controllers
{
    [Authorize]
    public class ReviewController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService _notificationService;

        public ReviewController(
            IRepositoryWrapper repositoryWrapper,
            UserManager<ApplicationUser> userManager,
            NotificationService notificationService)
        {
            _repositoryWrapper = repositoryWrapper;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        // GET: Review
        [Authorize(Roles = "Admin,FinancialOfficer")]
        public IActionResult Index()
        {
            var reviews = _repositoryWrapper.Review.FindAll()
                .Include(review => review.User)
                .Include(review => review.Accommodation)
                .OrderByDescending(review => review.CreatedAt)
                .ToList();
            return View(reviews);
        }

        // GET: Review/Details/5
        [Authorize(Roles = "Admin,FinancialOfficer")]
        public IActionResult Details(int id)
        {
            var review = _repositoryWrapper.Review.FindByCondition(item => item.Id == id)
                .Include(item => item.User)
                .Include(item => item.Accommodation)
                .FirstOrDefault();
            if (review == null)
            {
                return NotFound();
            }

            return View(review);
        }

        [HttpGet]
        [Authorize(Roles = "Student,Staff")]
        public IActionResult Create()
        {
            PopulateAccommodations();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Student,Staff")]
        public async Task<IActionResult> Create(int accommodationId, int rating, string title, string comment)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (rating < 1 || rating > 5)
            {
                ModelState.AddModelError(nameof(rating), "Choose a rating between 1 and 5.");
            }
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(comment) || title.Length > 200 || comment.Length > 2000)
            {
                ModelState.AddModelError(string.Empty, "Provide a title and comment within the permitted length.");
            }

            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(item => item.Id == accommodationId).FirstOrDefault();
            if (accommodation == null)
            {
                ModelState.AddModelError(nameof(accommodationId), "Choose a valid accommodation.");
            }
            if (!ModelState.IsValid)
            {
                PopulateAccommodations(accommodationId);
                return View();
            }

            var review = new Review
            {
                UserId = user.Id,
                AccommodationId = accommodationId,
                Rating = rating,
                Title = title.Trim(),
                Comment = comment.Trim(),
                // Retained for database compatibility. Reviews now publish immediately.
                IsApproved = true
            };
            _repositoryWrapper.Review.Create(review);
            _repositoryWrapper.Save();

            _notificationService.CreateActivityNotification(user.Id, "Feedback posted", "Your rating and review are now visible on the residence page.", $"/Accommodation/Details/{accommodationId}", review.Id);
            _notificationService.CreateRoleActivityNotification("Admin", "New customer feedback", $"{user.FirstName} {user.LastName} posted a review for {accommodation!.Name}.", $"/Review/Details/{review.Id}", review.Id);
            TempData["StatusMessage"] = "Your feedback has been posted to the residence page.";
            return RedirectToAction("Details", "Accommodation", new { id = accommodationId });
        }

        private void PopulateAccommodations(int? selectedAccommodationId = null)
        {
            ViewBag.Accommodations = new SelectList(
                _repositoryWrapper.Accommodation.FindAll().OrderBy(accommodation => accommodation.Name).ToList(),
                "Id",
                "Name",
                selectedAccommodationId);
        }
    }
}
