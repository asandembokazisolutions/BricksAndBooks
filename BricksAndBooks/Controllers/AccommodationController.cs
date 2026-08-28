using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Data;
using BricksAndBooks.Models;

namespace BricksAndBooks.Controllers
{
    public class AccommodationController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly IWebHostEnvironment _environment;

        public AccommodationController(IRepositoryWrapper repositoryWrapper, IWebHostEnvironment environment)
        {
            _repositoryWrapper = repositoryWrapper;
            _environment = environment;
        }

        // GET: Accommodation
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            var accommodations = _repositoryWrapper.Accommodation.FindAll().Include(a => a.Units).ToList();
            return View(accommodations);
        }

        // GET: Accommodation/Details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var accommodation = _repositoryWrapper.Accommodation.FindAll().Include(a => a.Units).FirstOrDefault(a => a.Id == id);
            if (accommodation == null)
            {
                return NotFound();
            }

            ViewBag.ResidenceReviews = _repositoryWrapper.Review.FindAll()
                .Where(review => review.AccommodationId == id)
                .Include(review => review.User)
                .OrderByDescending(review => review.CreatedAt)
                .ToList();

            return View(accommodation);
        }

        // GET: Accommodation/Create
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: Accommodation/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(string name, string address, string description, List<IFormFile>? images)
        {
            if (ModelState.IsValid)
            {
                var accommodation = new Accommodation { Name = name, Address = address, Description = description, ImageUrls = await SaveImagesAsync(images) };
                _repositoryWrapper.Accommodation.Create(accommodation);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index));
            }
            return View();
        }

        // GET: Accommodation/Edit/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == id).FirstOrDefault();
            if (accommodation == null)
            {
                return NotFound();
            }

            return View(accommodation);
        }

        // POST: Accommodation/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, string name, string address, string description, List<IFormFile>? images)
        {
            if (id <= 0)
            {
                return NotFound();
            }

            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == id).FirstOrDefault();
            if (accommodation == null)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                accommodation.Name = name;
                accommodation.Address = address;
                accommodation.Description = description;
                var newImages = await SaveImagesAsync(images);
                if (!string.IsNullOrWhiteSpace(newImages))
                {
                    accommodation.ImageUrls = string.Join('|', new[] { accommodation.ImageUrls, newImages }.Where(value => !string.IsNullOrWhiteSpace(value)));
                }
                accommodation.UpdatedAt = System.DateTime.UtcNow;

                _repositoryWrapper.Accommodation.Update(accommodation);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index));
            }
            return View(accommodation);
        }

        // GET: Accommodation/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == id).FirstOrDefault();
            if (accommodation == null)
            {
                return NotFound();
            }

            return View(accommodation);
        }

        // POST: Accommodation/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var accommodation = _repositoryWrapper.Accommodation.FindByCondition(a => a.Id == id).FirstOrDefault();
            if (accommodation != null)
            {
                _repositoryWrapper.Accommodation.Delete(accommodation);
                _repositoryWrapper.Save();
                return RedirectToAction(nameof(Index));
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<string?> SaveImagesAsync(IEnumerable<IFormFile>? images)
        {
            if (images == null)
            {
                return null;
            }

            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "accommodations");
            Directory.CreateDirectory(uploadRoot);
            var paths = new List<string>();
            foreach (var image in images.Where(file => file.Length > 0).Take(10))
            {
                var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
                if (!allowed.Contains(extension) || image.Length > 5 * 1024 * 1024)
                {
                    continue;
                }

                var fileName = $"{Guid.NewGuid():N}{extension}";
                var physicalPath = Path.Combine(uploadRoot, fileName);
                await using var stream = System.IO.File.Create(physicalPath);
                await image.CopyToAsync(stream);
                paths.Add($"/uploads/accommodations/{fileName}");
            }
            return paths.Count == 0 ? null : string.Join('|', paths);
        }
    }
}