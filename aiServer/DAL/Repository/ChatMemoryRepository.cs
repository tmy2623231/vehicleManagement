using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using vehicleManagement.aiServer.DAL.Model;

namespace vehicleManagement.aiServer.DAL.Repository
{
    /// <summary>
    /// 记忆仓储实现
    /// </summary>
    public class ChatMemoryRepository
    {
        private readonly IDbContextFactory<AiServerDbContext> _dbFactory;

        public ChatMemoryRepository(IDbContextFactory<AiServerDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<IEnumerable<ChatMemory>> GetAllAsync()
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<ChatMemory>)db.ChatMemories)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<ChatMemory>> SearchAsync(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<ChatMemory>();

            var kw = keyword.Trim();
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<ChatMemory>)db.ChatMemories)
                .Where(m => m.Key.Contains(kw) || m.Content.Contains(kw))
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .ToListAsync();
        }

        public async Task<ChatMemory> AddAsync(ChatMemory memory)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.ChatMemories.Add(memory);
            await db.SaveChangesAsync();
            return memory;
        }

        public async Task<ChatMemory> UpdateAsync(ChatMemory memory)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.ChatMemories.Update(memory);
            await db.SaveChangesAsync();
            return memory;
        }

        public async Task<ChatMemory> GetByKeyAsync(string key)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            return await ((IQueryable<ChatMemory>)db.ChatMemories)
                .FirstOrDefaultAsync(m => m.Key == key);
        }

        public async Task<bool> DeleteAsync(int id)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var memory = await db.ChatMemories.FindAsync(id);
            if (memory == null) return false;

            db.ChatMemories.Remove(memory);
            await db.SaveChangesAsync();
            return true;
        }
    }
}
