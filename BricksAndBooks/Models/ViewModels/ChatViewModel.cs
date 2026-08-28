using BricksAndBooks.Models;

namespace BricksAndBooks.Models.ViewModels
{
    public class ChatContactViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = "User";
        public string Initials { get; set; } = "U";
        public string LastMessage { get; set; } = string.Empty;
        public DateTime? LastSentAt { get; set; }
        public int UnreadCount { get; set; }
    }

    public class ChatViewModel
    {
        public string CurrentUserId { get; set; } = string.Empty;
        public List<ChatContactViewModel> Contacts { get; set; } = new();
        public ChatContactViewModel? SelectedContact { get; set; }
        public List<ChatMessage> Messages { get; set; } = new();
    }
}
