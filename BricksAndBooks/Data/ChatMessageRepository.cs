using BricksAndBooks.Models;

namespace BricksAndBooks.Data
{
    public class ChatMessageRepository : RepositoryBase<ChatMessage>, IChatMessageRepository
    {
        public ChatMessageRepository(BricksAndBooks.Data.DataAccess.AppDbContext dbContext)
            : base(dbContext)
        {
        }
    }
}