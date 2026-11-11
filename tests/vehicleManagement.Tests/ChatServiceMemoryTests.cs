using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using vehicleManagement.aiServer.BLL;
using vehicleManagement.aiServer.BLL.TextToSql;
using vehicleManagement.aiServer.DAL.Repository;
using vehicleManagement.BLL.Common;
using vehicleManagement.BLL.Dashboard;
using vehicleManagement.BLL.Department;
using vehicleManagement.BLL.Personnel;
using vehicleManagement.BLL.UsageRecord;
using vehicleManagement.BLL.Vehicle;
using vehicleManagement.DAL;
using vehicleManagement.DAL.Repository;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>P2-5 回归：SendMessageAsync 返回前必须已完成记忆提取并落库。</summary>
    public class ChatServiceMemoryTests : IDisposable
    {
        private readonly TestDbFactory _dbFactory = new();
        private readonly TestAiDbFactory _aiFactory = new();

        public ChatServiceMemoryTests()
        {
            using var db = _dbFactory.CreateDbContext();
            TestData.SeedBasicsAsync(db).GetAwaiter().GetResult();
        }

        private ChatService BuildChatService(IChatCompletionService completion)
        {
            var user = StubCurrentUser.Admin();
            var upload = new FileUploadService(new FakeWebHostEnv());
            var audit = new AuditService(_dbFactory, user, NullLogger<AuditService>.Instance);

            var vehicleService = new VehicleService(
                new VehicleRepository(_dbFactory), new DepartmentRepository(_dbFactory), _dbFactory,
                upload, user, audit, NullLogger<VehicleService>.Instance);
            var departmentService = new DepartmentService(
                new DepartmentRepository(_dbFactory), _dbFactory, audit);
            var personnelService = new PersonnelService(
                new PersonnelRepository(_dbFactory), new DepartmentRepository(_dbFactory),
                _dbFactory, user, audit);
            var usageRecordService = new UsageRecordService(
                new UsageRecordRepository(_dbFactory), _dbFactory, upload, user, audit,
                NullLogger<UsageRecordService>.Instance);
            var dashboardService = new DashboardService(_dbFactory, user);

            var dataQuerier = new DataQuerier(
                vehicleService, departmentService, personnelService, usageRecordService, dashboardService);

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(completion);
            var kernel = builder.Build();

            var memoryService = new MemoryService(
                new ChatMemoryRepository(_aiFactory), NullLogger<MemoryService>.Instance);

            // Text-to-SQL 副路径默认关闭（AiServerOptions.TextToSqlEnabled=false），主路径行为不变
            var aiOptions = new AiServerOptions();
            var textToSql = new TextToSqlService(
                _dbFactory, kernel, aiOptions, NullLogger<TextToSqlService>.Instance);

            return new ChatService(
                new ChatSessionRepository(_aiFactory),
                new ChatMessageRepository(_aiFactory),
                memoryService,
                dataQuerier,
                textToSql,
                aiOptions,
                kernel,
                NullLogger<ChatService>.Instance);
        }

        [Fact]
        public async Task SendMessageAsync_ReplyPersistedAndMemoryWrittenBeforeReturn()
        {
            var service = BuildChatService(new FakeChatCompletionService());
            var session = await service.CreateSessionAsync("验收会话");

            var reply = await service.SendMessageAsync(session.Id, "你好");

            Assert.Equal("assistant", reply.Role);
            Assert.Equal(FakeChatCompletionService.FixedReply, reply.Content);

            // 方法返回时记忆须已在库（fire-and-forget 会在此失败）
            using var aiDb = _aiFactory.CreateDbContext();
            var memoryCount = await aiDb.ChatMemories.CountAsync(m => m.SessionId == session.Id);
            Assert.True(memoryCount >= 1, "SendMessageAsync 返回时记忆必须已落库（P2-5：不得 fire-and-forget）");

            // 一问一答两条消息都在，且按时间升序取出
            var messages = await service.GetMessagesAsync(session.Id);
            var list = System.Linq.Enumerable.ToList(messages);
            Assert.Equal(2, list.Count);
            Assert.Equal("user", list[0].Role);
            Assert.Equal("assistant", list[1].Role);
        }

        [Fact]
        public async Task SendMessageAsync_LlmThrows_ReturnsFriendlyTextAndStillPersistsMemory()
        {
            // P2-5 第二半：异常必须被分类成面向用户的文案，而不是把 ex.Message 抛到页面
            var service = BuildChatService(new FakeChatCompletionService(new InvalidOperationException("connect refused to 10.200.8.2:11434")));
            var session = await service.CreateSessionAsync();

            var reply = await service.SendMessageAsync(session.Id, "你好");

            Assert.Equal("AI 服务暂时不可用，请稍后重试或联系管理员。", reply.Content);
            Assert.DoesNotContain("10.200.8.2", reply.Content);          // 技术细节不外泄
            Assert.DoesNotContain("connect refused", reply.Content);

            using var aiDb = _aiFactory.CreateDbContext();
            Assert.True(await aiDb.ChatMemories.AnyAsync(m => m.SessionId == session.Id));
        }

        public void Dispose()
        {
            _aiFactory.Dispose();
            _dbFactory.Dispose();
        }
    }
}
