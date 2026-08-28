using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BricksAndBooks.Data;
using BricksAndBooks.Models;
using BricksAndBooks.Infrastructure;
using BricksAndBooks.Models.ViewModels;

namespace BricksAndBooks.Controllers
{
    [Authorize] // Any authenticated user can use chat
    public class ChatController : Controller
    {
        private readonly IRepositoryWrapper _repositoryWrapper;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService _notificationService;

        public ChatController(IRepositoryWrapper repositoryWrapper, UserManager<ApplicationUser> userManager, NotificationService notificationService)
        {
            _repositoryWrapper = repositoryWrapper;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        // GET: Chat
        public async Task<IActionResult> Index(string? contactId = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var chatMessages = _repositoryWrapper.ChatMessage.FindAll()
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .Where(m => m.SenderUserId == user.Id || m.RecipientUserId == user.Id)
                .OrderBy(m => m.SentAt)
                .ToList();

            var users = await _userManager.Users
                .Where(u => u.Id != user.Id)
                .OrderBy(u => u.FirstName)
                .ThenBy(u => u.LastName)
                .ToListAsync();

            var contacts = users.Select(other =>
            {
                var thread = chatMessages.Where(m =>
                    (m.SenderUserId == user.Id && m.RecipientUserId == other.Id) ||
                    (m.SenderUserId == other.Id && m.RecipientUserId == user.Id)).ToList();
                var last = thread.LastOrDefault();
                return new ChatContactViewModel
                {
                    Id = other.Id,
                    DisplayName = DisplayName(other),
                    Initials = Initials(other),
                    LastMessage = last?.Content ?? "Start a conversation",
                    LastSentAt = last?.SentAt,
                    UnreadCount = thread.Count(m => m.RecipientUserId == user.Id && !m.IsRead)
                };
            }).OrderByDescending(c => c.LastSentAt.HasValue).ThenByDescending(c => c.LastSentAt).ThenBy(c => c.DisplayName).ToList();

            var selectedContact = contacts.FirstOrDefault(c => c.Id == contactId) ?? contacts.FirstOrDefault();
            var selectedMessages = selectedContact == null
                ? new List<ChatMessage>()
                : chatMessages.Where(m =>
                    (m.SenderUserId == user.Id && m.RecipientUserId == selectedContact.Id) ||
                    (m.SenderUserId == selectedContact.Id && m.RecipientUserId == user.Id)).ToList();

            var unreadMessages = selectedMessages.Where(m => m.RecipientUserId == user.Id && !m.IsRead).ToList();
            foreach (var message in unreadMessages)
            {
                // The message was loaded with Sender and Recipient using the same DbContext.
                // It is already tracked, so calling Update(message) would re-attach its user
                // navigation graph and can create duplicate ApplicationUser tracking entries.
                message.IsRead = true;
                message.ReadAt = DateTime.UtcNow;
            }
            if (unreadMessages.Count > 0)
            {
                _repositoryWrapper.Save();
                // The contact view model was built before the read-state update.
                // Refresh the visible badge for the conversation just opened.
                if (selectedContact != null)
                {
                    selectedContact.UnreadCount = 0;
                }
            }

            return View(new ChatViewModel
            {
                CurrentUserId = user.Id,
                Contacts = contacts,
                SelectedContact = selectedContact,
                Messages = selectedMessages
            });
        }

        // GET: Chat/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            var chatMessage = _repositoryWrapper.ChatMessage.FindAll()
                .Include(m => m.Sender)
                .Include(m => m.Recipient)
                .FirstOrDefault(m => m.Id == id);
            if (chatMessage == null)
            {
                return NotFound();
            }
            if (user == null || (chatMessage.SenderUserId != user.Id && chatMessage.RecipientUserId != user.Id))
            {
                return Forbid();
            }
            if (chatMessage.RecipientUserId == user.Id && !chatMessage.IsRead)
            {
                // chatMessage is already tracked by this DbContext; save the scalar changes
                // without re-attaching the Sender and Recipient navigation graph.
                chatMessage.IsRead = true;
                chatMessage.ReadAt = DateTime.UtcNow;
                _repositoryWrapper.Save();
            }
            return View(chatMessage);
        }

        // The old standalone compose route now opens the conversation workspace.
        public IActionResult Create()
        {
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(string recipientUserId, string content)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            var recipient = await _userManager.FindByIdAsync(recipientUserId);
            if (recipient == null || recipient.Id == user.Id || string.IsNullOrWhiteSpace(content) || content.Trim().Length > 2000)
            {
                TempData["ChatError"] = "Select a valid person and enter a message between 1 and 2,000 characters.";
                return RedirectToAction(nameof(Index), new { contactId = recipientUserId });
            }

            var chatMessage = new ChatMessage
            {
                SenderUserId = user.Id,
                RecipientUserId = recipient.Id,
                Content = content.Trim(),
                IsRead = false,
                SentAt = DateTime.UtcNow
            };
            _repositoryWrapper.ChatMessage.Create(chatMessage);
            _repositoryWrapper.Save();

            _notificationService.CreateNewMessageNotification(recipient.Id, chatMessage.Id, DisplayName(user));
            return RedirectToAction(nameof(Index), new { contactId = recipient.Id });
        }

        // POST: Chat/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string recipientUserId, string content)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 2000)
            {
                ModelState.AddModelError(string.Empty, "Enter a message between 1 and 2,000 characters.");
            }
            var recipient = await _userManager.FindByIdAsync(recipientUserId);
            if (recipient == null || recipient.Id == user.Id)
            {
                ModelState.AddModelError(string.Empty, "Select a valid message recipient.");
            }

            if (ModelState.IsValid)
            {
                var chatMessage = new ChatMessage
                {
                    SenderUserId = user.Id,
                    RecipientUserId = recipientUserId,
                    Content = content,
                    IsRead = false
                };
                _repositoryWrapper.ChatMessage.Create(chatMessage);
                _repositoryWrapper.Save();

                // Create a notification for the recipient about the new message
                if (await _userManager.FindByIdAsync(recipientUserId) != null)
                {
                    var senderName = user.UserName ?? user.Email ?? "Unknown";
                    _notificationService.CreateNewMessageNotification(
                        recipientUserId,
                        chatMessage.Id,
                        senderName);
                }

                return RedirectToAction(nameof(Index));
            }
            ViewBag.Recipients = _userManager.Users
                .Where(u => u.Id != user.Id)
                .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
                .ToList();
            return View();
        }

        private static string DisplayName(ApplicationUser user)
        {
            var name = $"{user.FirstName} {user.LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? (user.Email ?? user.UserName ?? "User") : name;
        }

        private static string Initials(ApplicationUser user)
        {
            var name = DisplayName(user).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(name.Take(2).Select(part => char.ToUpperInvariant(part[0])));
        }
    }
}