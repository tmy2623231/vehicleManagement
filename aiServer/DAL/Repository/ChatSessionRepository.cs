using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.aiServer.DAL.Model;

namespace vehicleManagement.aiServer.DAL.Repository
{
    /// <summary>
    /// 会话仓储实现
    /// </summary>
    public class ChatSessionRepository
    {
        private readonly IDbContextFactory<AiServerDbContext> _dbFactory;

        public ChatSessionRepository(IDbContextFactory<AiServerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<ChatSession>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<ChatSession>)db.ChatSessions)
                .OrderByDescending(s => s.UpdatedAt)
                .ToListAsync();
        }

        public async Task<ChatSession> GetByIdAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await db.ChatSessions.FindAsync(id);
        }

        public async Task<ChatSession> AddAsync(ChatSession session)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async Task<ChatSession> UpdateAsync(ChatSession session)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            session.UpdatedAt = System.DateTime.Now;
            db.ChatSessions.Update(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var session = await db.ChatSessions.FindAsync(id);
            if (session == null) return false;

            db.ChatSessions.Remove(session);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ExistsAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<ChatSession>)db.ChatSessions)
                .AnyAsync(s => s.Id == id);
        }
    }
}
