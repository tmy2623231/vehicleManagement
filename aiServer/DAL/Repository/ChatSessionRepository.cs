using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.aiServer.DAL.Model;
using vehicleManagement.DAL.Common;

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
                .AsNoTracking()
                .OrderByDescending(s => s.UpdatedAt)
                .ToListAsync();
        }

        // 不加 AsNoTracking：调用方会「读-改-写」，脱跟踪后 SetValues 找不到原实体
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

        public async Task<ChatSession> UpdateAsync(ChatSession patch)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var existing = await db.ChatSessions.FindAsync(patch.Id);
            if (existing is null) return patch;      // 保持现有「不抛异常」返回契约
            patch.UpdatedAt = ChinaTime.Now;
            db.Entry(existing).CurrentValues.SetValues(patch);   // 只覆盖标量列，导航集合（Messages）一律不动
            await db.SaveChangesAsync();
            return existing;
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
