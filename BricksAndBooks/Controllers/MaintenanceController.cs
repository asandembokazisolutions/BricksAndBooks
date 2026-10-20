using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Data;
using BricksAndBooks.Models;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models.ViewModels;

namespace BricksAndBooks.Controllers
{
    [Authorize]
    public class MaintenanceController : Controller
    {
        private static readonly string[] AllowedAttachmentExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".pdf", ".doc", ".docx" };
        private const long MaximumAttachmentSize = 5 * 1024 * 1024;
        private const int MaximumAttachmentCount = 5;

        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService _notificationService;
        private readonly AuditService _auditService;
        private readonly IWebHostEnvironment _environment;

        public MaintenanceController(
            IRepositoryWrapper repositoryWrapper,
            UserManager<ApplicationUser> userManager,
            NotificationService notificationService,
            AuditService auditService,
            IWebHostEnvironment environment)
        {
            _repositoryWrapper = repositoryWrapper;
            _userManager = userManager;
            _notificationService = notificationService;
            _auditService = auditService;
            _environment = environment;
        }

        // GET: Maintenance
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            IQueryable<MaintenanceRequest> maintenanceRequests = _repositoryWrapper.MaintenanceRequest.FindAll()
                .Include(request => request.User)
                .Include(request => request.Unit)
                .Include(request => request.AssignedToUser);

            if (User.IsInRole("MaintenanceWorker"))
            {
                maintenanceRequests = maintenanceRequests.Where(request => request.AssignedToUserId == user.Id);
            }
            else if (!User.IsInRole("Admin") && !User.IsInRole("FinancialOfficer"))
            {
                maintenanceRequests = maintenanceRequests.Where(request => request.UserId == user.Id);
            }

            return View(await maintenanceRequests.OrderByDescending(request => request.SubmittedAt).ToListAsync());
        }

        // GET: Maintenance/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var maintenanceRequest = await GetMaintenanceRequestAsync(id);
            if (maintenanceRequest == null)
            {
                return NotFound();
            }
            if (user == null || (!User.IsInRole("Admin") && !User.IsInRole("FinancialOfficer")
                && !(User.IsInRole("MaintenanceWorker") && maintenanceRequest.AssignedToUserId == user.Id)
                && maintenanceRequest.UserId != user.Id))
            {
                return Forbid();
            }

            return View(maintenanceRequest);
        }

        // GET: Maintenance/Create
        [Authorize]
        public async Task<IActionResult> Create()
        {
            if (!CanSubmitCustomerMaintenanceRequest())
            {
                return Forbid();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            await PopulateUnitsAsync(user.Id);
            return View(new MaintenanceRequest());
        }

        // POST: Maintenance/Create
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int unitId, MaintenanceCategory category, string description, List<IFormFile>? attachments)
        {
            if (!CanSubmitCustomerMaintenanceRequest())
            {
                return Forbid();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var ownsUnit = _repositoryWrapper.Occupancy.FindAll()
                .Any(occupancy => occupancy.UserId == user.Id
                    && occupancy.UnitId == unitId
                    && (occupancy.EndDate == null || occupancy.EndDate >= DateTime.UtcNow));
            if (!ownsUnit)
            {
                ModelState.AddModelError(nameof(unitId), "You can only report an issue for your current allocated unit.");
            }
            if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 2000)
            {
                ModelState.AddModelError(nameof(description), "Describe the issue in between 1 and 2,000 characters.");
            }

            ValidateAttachments(attachments);
            if (!ModelState.IsValid)
            {
                await PopulateUnitsAsync(user.Id);
                return View(new MaintenanceRequest
                {
                    UnitId = unitId,
                    Category = category,
                    Description = description?.Trim() ?? string.Empty
                });
            }

            var maintenanceRequest = new MaintenanceRequest
            {
                UserId = user.Id,
                UnitId = unitId,
                Category = category,
                Description = description.Trim(),
                AttachmentUrls = await SaveAttachmentsAsync(attachments),
                Status = MaintenanceStatus.Submitted
            };
            _repositoryWrapper.MaintenanceRequest.Create(maintenanceRequest);
            _repositoryWrapper.Save();

            _auditService.Record(User, "Create", "MaintenanceRequest", maintenanceRequest.Id, $"Submitted {category} issue for unit #{unitId}.");
            _notificationService.CreateActivityNotification(user.Id, "Maintenance request submitted", "Your maintenance request was submitted and is awaiting an update.", $"/Maintenance/Details/{maintenanceRequest.Id}", maintenanceRequest.Id);

            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            var financialOfficers = await _userManager.GetUsersInRoleAsync("FinancialOfficer");
            foreach (var notifiedUser in admins.Concat(financialOfficers).Distinct())
            {
                _notificationService.CreateMaintenanceUpdateNotification(notifiedUser.Id, maintenanceRequest.Id, maintenanceRequest.Status.ToString());
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Maintenance/Edit/5
        [Authorize(Roles = "Admin,FinancialOfficer,MaintenanceWorker")]
        public async Task<IActionResult> Edit(int id)
        {
            var maintenanceRequest = await GetMaintenanceRequestAsync(id);
            if (maintenanceRequest == null)
            {
                return NotFound();
            }

            var isAssignedWorker = User.IsInRole("MaintenanceWorker");
            if (isAssignedWorker && maintenanceRequest.AssignedToUserId != _userManager.GetUserId(User)) return Forbid();
            ViewBag.IsAssignedWorker = isAssignedWorker;
            await PopulateEditFormAsync(maintenanceRequest);
            return View(maintenanceRequest);
        }

        // POST: Maintenance/Edit/5
        [HttpPost]
        [Authorize(Roles = "Admin,FinancialOfficer,MaintenanceWorker")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, MaintenanceStatus status, string? assignedToUserId, List<IFormFile>? attachments)
        {
            var maintenanceRequest = await GetMaintenanceRequestAsync(id);
            if (maintenanceRequest == null)
            {
                return NotFound();
            }

            var isAssignedWorker = User.IsInRole("MaintenanceWorker");
            if (isAssignedWorker && maintenanceRequest.AssignedToUserId != _userManager.GetUserId(User)) return Forbid();
            if (isAssignedWorker) assignedToUserId = maintenanceRequest.AssignedToUserId;
            if (!isAssignedWorker && !string.IsNullOrWhiteSpace(assignedToUserId) && !await IsAssignableUserAsync(assignedToUserId, maintenanceRequest.Category))
            {
                ModelState.AddModelError(nameof(assignedToUserId), "Choose a valid maintenance worker, administrator, or financial officer to assign this request.");
            }
            ValidateAttachments(attachments);

            if (!ModelState.IsValid)
            {
                maintenanceRequest.Status = status;
                maintenanceRequest.AssignedToUserId = assignedToUserId;
                ViewBag.IsAssignedWorker = isAssignedWorker;
                await PopulateEditFormAsync(maintenanceRequest);
                return View(maintenanceRequest);
            }

            var originalStatus = maintenanceRequest.Status;
            var originalAssignedToUserId = maintenanceRequest.AssignedToUserId;
            var wasAlreadyAssigned = !string.IsNullOrWhiteSpace(originalAssignedToUserId);

            maintenanceRequest.Status = status;
            maintenanceRequest.AssignedToUserId = string.IsNullOrWhiteSpace(assignedToUserId) ? null : assignedToUserId;
            if (!string.IsNullOrWhiteSpace(maintenanceRequest.AssignedToUserId) && maintenanceRequest.AssignedToUserId != originalAssignedToUserId)
            {
                maintenanceRequest.AssignedAt = DateTime.UtcNow;
            }
            else if (string.IsNullOrWhiteSpace(maintenanceRequest.AssignedToUserId) && wasAlreadyAssigned)
            {
                maintenanceRequest.AssignedAt = null;
            }

            var uploadedAttachments = await SaveAttachmentsAsync(attachments);
            if (!string.IsNullOrWhiteSpace(uploadedAttachments))
            {
                maintenanceRequest.AttachmentUrls = string.Join('|', new[] { maintenanceRequest.AttachmentUrls, uploadedAttachments }
                    .Where(path => !string.IsNullOrWhiteSpace(path)));
            }

            maintenanceRequest.ResolvedAt = status == MaintenanceStatus.Resolved ? DateTime.UtcNow : null;
            maintenanceRequest.UpdatedAt = DateTime.UtcNow;
            _repositoryWrapper.MaintenanceRequest.Update(maintenanceRequest);
            _repositoryWrapper.Save();

            _auditService.Record(User, "Update", "MaintenanceRequest", maintenanceRequest.Id, $"Changed status from {originalStatus} to {status}; assigned user: {maintenanceRequest.AssignedToUserId ?? "not assigned"}.");

            if (originalStatus != status)
            {
                _notificationService.CreateMaintenanceUpdateNotification(maintenanceRequest.UserId, maintenanceRequest.Id, status.ToString());
            }

            if (originalAssignedToUserId != maintenanceRequest.AssignedToUserId && !string.IsNullOrWhiteSpace(maintenanceRequest.AssignedToUserId))
            {
                var assignedUser = await _userManager.FindByIdAsync(maintenanceRequest.AssignedToUserId);
                if (assignedUser != null)
                {
                    _notificationService.CreateMaintenanceUpdateNotification(maintenanceRequest.UserId, maintenanceRequest.Id, $"Assigned to {assignedUser.UserName}");
                }
            }

            if (status == MaintenanceStatus.Resolved)
            {
                _notificationService.CreateMaintenanceUpdateNotification(maintenanceRequest.UserId, maintenanceRequest.Id, "Resolved");
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,FinancialOfficer")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSpecialist(int id, [Bind(Prefix = "Worker")] MaintenanceWorkerCreateViewModel worker)
        {
            var request = await GetMaintenanceRequestAsync(id);
            if (request == null) return NotFound();
            if (worker.Specialty != request.Category)
                ModelState.AddModelError("Worker.Specialty", $"Choose the {request.Category} specialty for this request.");
            if (!ModelState.IsValid)
            {
                ViewBag.IsAssignedWorker = false;
                await PopulateEditFormAsync(request);
                ViewBag.WorkerErrors = ModelState.Where(pair => pair.Key.StartsWith("Worker") && pair.Value?.Errors.Count > 0)
                    .SelectMany(pair => pair.Value!.Errors).Select(error => error.ErrorMessage).ToList();
                return View("Edit", request);
            }
            var email = worker.Email.Trim().ToLowerInvariant();
            var specialist = new ApplicationUser
            {
                UserName = email, Email = email, FirstName = worker.FirstName.Trim(), LastName = worker.LastName.Trim(),
                IsMaintenanceWorker = true, MaintenanceSpecialty = worker.Specialty, EmailConfirmed = true
            };
            var created = await _userManager.CreateAsync(specialist, worker.Password);
            if (created.Succeeded) created = await _userManager.AddToRoleAsync(specialist, "MaintenanceWorker");
            if (!created.Succeeded)
            {
                foreach (var error in created.Errors) ModelState.AddModelError("Worker.Password", error.Description);
                ViewBag.IsAssignedWorker = false;
                await PopulateEditFormAsync(request);
                ViewBag.WorkerErrors = ModelState.Where(pair => pair.Key.StartsWith("Worker") && pair.Value?.Errors.Count > 0)
                    .SelectMany(pair => pair.Value!.Errors).Select(error => error.ErrorMessage).ToList();
                return View("Edit", request);
            }
            request.AssignedToUserId = specialist.Id;
            request.AssignedAt = DateTime.UtcNow;
            request.Status = MaintenanceStatus.Assigned;
            request.UpdatedAt = DateTime.UtcNow;
            _repositoryWrapper.MaintenanceRequest.Update(request);
            _repositoryWrapper.Save();
            _auditService.Record(User, "Create and assign", "MaintenanceWorker", request.Id, $"Created {worker.Specialty} specialist {specialist.Email} and assigned to request #{request.Id}.");
            _notificationService.CreateMaintenanceUpdateNotification(request.UserId, request.Id, $"Assigned to {specialist.FirstName} {specialist.LastName} ({worker.Specialty} specialist)");
            TempData["StatusMessage"] = $"{specialist.FirstName} {specialist.LastName} was created as a {worker.Specialty} specialist and assigned to request #{request.Id}.";
            return RedirectToAction(nameof(Edit), new { id });
        }
        // GET: Maintenance/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var maintenanceRequest = await GetMaintenanceRequestAsync(id);
            if (maintenanceRequest == null)
            {
                return NotFound();
            }

            return View(maintenanceRequest);
        }

        // POST: Maintenance/Delete/5
        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var maintenanceRequest = await GetMaintenanceRequestAsync(id);
            if (maintenanceRequest != null)
            {
                _repositoryWrapper.MaintenanceRequest.Delete(maintenanceRequest);
                _repositoryWrapper.Save();
            }

            return RedirectToAction(nameof(Index));
        }

        private bool CanSubmitCustomerMaintenanceRequest()
        {
            var isCustomer = User.IsInRole("Student") || User.IsInRole("Staff");
            var isPrivilegedOperator = User.IsInRole("Admin") || User.IsInRole("FinancialOfficer");
            return isCustomer && !isPrivilegedOperator;
        }

        private async Task<MaintenanceRequest?> GetMaintenanceRequestAsync(int id)
        {
            return await _repositoryWrapper.MaintenanceRequest.FindAll()
                .Include(request => request.User)
                .Include(request => request.Unit)
                .Include(request => request.AssignedToUser)
                .FirstOrDefaultAsync(request => request.Id == id);
        }

        private async Task PopulateUnitsAsync(string userId)
        {
            var unitIds = _repositoryWrapper.Occupancy.FindAll()
                .Where(occupancy => occupancy.UserId == userId && (occupancy.EndDate == null || occupancy.EndDate >= DateTime.UtcNow))
                .Select(occupancy => occupancy.UnitId)
                .ToHashSet();

            var units = _repositoryWrapper.Unit.FindAll()
                .Where(unit => unitIds.Contains(unit.Id))
                .OrderBy(unit => unit.UnitNumber)
                .Select(unit => new { unit.Id, Label = $"Unit {unit.UnitNumber}" })
                .ToList();
            ViewBag.Units = new SelectList(units, "Id", "Label");
            await Task.CompletedTask;
        }

        private async Task PopulateEditFormAsync(MaintenanceRequest maintenanceRequest)
        {
            var unit = maintenanceRequest.Unit ?? await _repositoryWrapper.Unit.FindAll()
                .Include(item => item.Accommodation)
                .FirstOrDefaultAsync(item => item.Id == maintenanceRequest.UnitId);
            ViewBag.UnitLabel = unit == null
                ? $"Unit #{maintenanceRequest.UnitId}"
                : $"Unit {unit.UnitNumber}" + (string.IsNullOrWhiteSpace(unit.Accommodation?.Name) ? string.Empty : $" — {unit.Accommodation.Name}");

            // Only workers whose trade matches this request may be assigned.
            var assignees = await _userManager.Users
                .Where(user => user.IsMaintenanceWorker && user.MaintenanceSpecialty == maintenanceRequest.Category)
                .OrderBy(user => user.FirstName)
                .ThenBy(user => user.LastName)
                .ToListAsync();

            var assigneeOptions = new List<SelectListItem>
            {
                new SelectListItem { Value = string.Empty, Text = "Not assigned" }
            };
            assigneeOptions.AddRange(assignees.Select(user => new SelectListItem
            {
                Value = user.Id,
                Text = $"{user.FirstName} {user.LastName} — {user.MaintenanceSpecialty} specialist",
                Selected = user.Id == maintenanceRequest.AssignedToUserId
            }));
            ViewBag.Assignees = assigneeOptions;
            ViewBag.AssigneeCount = assignees.Count;
        }

        private async Task<bool> IsAssignableUserAsync(string userId, MaintenanceCategory requestCategory)
        {
            var user = await _userManager.FindByIdAsync(userId);
            return user != null && user.IsMaintenanceWorker && user.MaintenanceSpecialty == requestCategory;
        }

        private void ValidateAttachments(IEnumerable<IFormFile>? attachments)
        {
            var selectedFiles = attachments?.Where(file => file.Length > 0).ToList() ?? new List<IFormFile>();
            if (selectedFiles.Count > MaximumAttachmentCount)
            {
                ModelState.AddModelError("attachments", $"Choose no more than {MaximumAttachmentCount} attachments.");
            }

            foreach (var attachment in selectedFiles)
            {
                var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
                if (!AllowedAttachmentExtensions.Contains(extension))
                {
                    ModelState.AddModelError("attachments", "Only JPG, JPEG, PNG, WEBP, PDF, DOC, and DOCX files are allowed.");
                }
                else if (attachment.Length > MaximumAttachmentSize)
                {
                    ModelState.AddModelError("attachments", "Each attachment must be 5 MB or smaller.");
                }
            }
        }

        private async Task<string?> SaveAttachmentsAsync(IEnumerable<IFormFile>? attachments)
        {
            var selectedFiles = attachments?.Where(file => file.Length > 0).Take(MaximumAttachmentCount).ToList() ?? new List<IFormFile>();
            if (!selectedFiles.Any())
            {
                return null;
            }

            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "maintenance");
            Directory.CreateDirectory(uploadRoot);

            var paths = new List<string>();
            foreach (var attachment in selectedFiles)
            {
                var extension = Path.GetExtension(attachment.FileName).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid():N}{extension}";
                var physicalPath = Path.Combine(uploadRoot, fileName);
                await using var stream = System.IO.File.Create(physicalPath);
                await attachment.CopyToAsync(stream);
                paths.Add($"/uploads/maintenance/{fileName}");
            }

            return string.Join('|', paths);
        }
    }
}
