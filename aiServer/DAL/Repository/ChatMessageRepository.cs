using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.aiServer.DAL.Model;

namespace vehicleManagement.aiServer.DAL.Repository
{
    /// <summary>
    /// 消息仓储实现
    /// </summary>
    public class ChatMessageRepository
    {
        private readonly IDbContextFactory<AiServerDbContext> _dbFactory;

        public ChatMessageRepository(IDbContextFactory<AiServerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<ChatMessage>> GetBySessionIdAsync(int sessionId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<ChatMessage>)db.ChatMessages)
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<ChatMessage>> GetRecentBySessionIdAsync(int sessionId, int count)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<ChatMessage>)db.ChatMessages)
                .Where(m => m.SessionId == sessionId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(count)
                .OrderBy(m => m.CreatedAt)  // 最终按时间升序返回
                .ToListAsync();
        }

        public async Task<ChatMessage> AddAsync(ChatMessage message)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.ChatMessages.Add(message);
            await db.SaveChangesAsync();
            return message;
        }

        public async Task DeleteBySessionIdAsync(int sessionId)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var messages = await ((IQueryable<ChatMessage>)db.ChatMessages)
                .Where(m => m.SessionId == sessionId)
                .ToListAsync();
            db.ChatMessages.RemoveRange(messages);
            await db.SaveChangesAsync();
        }
    }
}
