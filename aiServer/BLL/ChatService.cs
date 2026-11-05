using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using vehicleManagement.aiServer.BLL.Enums;
using vehicleManagement.aiServer.BLL.Prompts;
using vehicleManagement.aiServer.DAL.Model;
using vehicleManagement.aiServer.DAL.Repository;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

#nullable enable

namespace vehicleManagement.aiServer.BLL
{
    /// <summary>
    /// AI 对话服务实现：集成 Semantic Kernel + Ollama + 意图识别 + 数据库查询 + 记忆管理
    /// </summary>
    public class ChatService
    {
        private readonly ChatSessionRepository _sessionRepo;
        private readonly ChatMessageRepository _messageRepo;
        private readonly MemoryService _memoryService;
        private readonly DataQuerier _dataQuerier;
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatCompletion;

        private const int MaxRecentMessages = 20;

        public ChatService(
            ChatSessionRepository sessionRepo,
            ChatMessageRepository messageRepo,
            MemoryService memoryService,
            DataQuerier dataQuerier,
            Kernel kernel)
        {
            _sessionRepo = sessionRepo;
            _messageRepo = messageRepo;
            _memoryService = memoryService;
            _dataQuerier = dataQuerier;
            _kernel = kernel;
            _chatCompletion = kernel.GetRequiredService<IChatCompletionService>();
        }

        // ==================== 会话管理 ====================

        public async Task<IEnumerable<ChatSession>> GetSessionsAsync()
            => await _sessionRepo.GetAllAsync();

        public async Task<ChatSession> CreateSessionAsync(string? title = null)
        {
            var session = new ChatSession
            {
                Title = title ?? "新对话",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            return await _sessionRepo.AddAsync(session);
        }

        public async Task<bool> DeleteSessionAsync(int sessionId)
        {
            var exists = await _sessionRepo.ExistsAsync(sessionId);
            if (!exists) return false;
            return await _sessionRepo.DeleteAsync(sessionId);
        }

        public async Task<IEnumerable<ChatMessage>> GetMessagesAsync(int sessionId)
            => await _messageRepo.GetBySessionIdAsync(sessionId);

        public async Task UpdateSessionTitleAsync(int sessionId, string title)
        {
            var session = await _sessionRepo.GetByIdAsync(sessionId);
            if (session != null)
            {
                session.Title = title;
                await _sessionRepo.UpdateAsync(session);
            }
        }

        // ==================== 记忆管理 ====================

        public async Task<IEnumerable<ChatMemory>> GetMemoriesAsync()
            => await _memoryService.GetAllMemoriesAsync();

        public async Task<bool> DeleteMemoryAsync(int id)
            => await _memoryService.DeleteMemoryAsync(id);

        // ==================== 核心对话 ====================

        /// <summary>
        /// 发送消息并获取 AI 回复
        /// 流程：保存消息 → 意图识别 → 查询数据库 → 构建上下文 → LLM 回复 → 保存回复
        /// </summary>
        public async Task<ChatMessage> SendMessageAsync(int sessionId, string content)
        {
            // 1. 保存用户消息
            var userMessage = new ChatMessage
            {
                SessionId = sessionId,
                Role = "user",
                Content = content,
                CreatedAt = DateTime.Now
            };
            await _messageRepo.AddAsync(userMessage);

            // 自动更新会话标题
            await UpdateSessionMetadataAsync(sessionId, content);

            // 2. 意图识别
            var (intent, keyword) = IntentDetector.Detect(content);

            // 3. 查询数据库
            var dataContext = await _dataQuerier.QueryAsync(intent, keyword);

            // 4. 构建聊天上下文
            var chatHistory = await BuildChatHistory(sessionId, content, dataContext);

            // 5. 调用 LLM
            var assistantContent = await CallLlmAsync(chatHistory);

            // 6. 保存回复
            var assistantMessage = new ChatMessage
            {
                SessionId = sessionId,
                Role = "assistant",
                Content = assistantContent,
                CreatedAt = DateTime.Now
            };
            await _messageRepo.AddAsync(assistantMessage);

            // 7. 异步提取记忆
            _ = Task.Run(async () =>
            {
                try { await _memoryService.ExtractAndStoreAsync(content, assistantContent, sessionId); }
                catch { /* 非关键路径 */ }
            });

            return assistantMessage;
        }

        // ==================== 私有辅助方法 ====================

        /// <summary>
        /// 更新会话的标题和更新时间
        /// </summary>
        private async Task UpdateSessionMetadataAsync(int sessionId, string content)
        {
            var session = await _sessionRepo.GetByIdAsync(sessionId);
            if (session == null) return;

            if (session.Title == "新对话" || string.IsNullOrEmpty(session.Title))
                session.Title = content.Length > 30 ? content[..30] + "..." : content;

            session.UpdatedAt = DateTime.Now;
            await _sessionRepo.UpdateAsync(session);
        }

        /// <summary>
        /// 构建 Semantic Kernel ChatHistory（系统提示词 + 数据注入 + 历史消息）
        /// </summary>
        private async Task<ChatHistory> BuildChatHistory(int sessionId, string currentContent, string? dataContext)
        {
            var chatHistory = new ChatHistory(SystemPrompts.Main);

            // 注入数据库查询结果
            if (!string.IsNullOrEmpty(dataContext))
                chatHistory.AddSystemMessage(SystemPrompts.BuildDataInjectionPrompt(dataContext));

            // 注入记忆上下文
            var memoryContext = await _memoryService.BuildMemoryContextAsync(currentContent);
            if (!string.IsNullOrEmpty(memoryContext))
                chatHistory.AddSystemMessage(memoryContext);

            // 加载最近历史消息
            var recentMessages = await _messageRepo.GetRecentBySessionIdAsync(sessionId, MaxRecentMessages);
            foreach (var msg in recentMessages)
            {
                if (msg.Role == "user")      chatHistory.AddUserMessage(msg.Content);
                if (msg.Role == "assistant") chatHistory.AddAssistantMessage(msg.Content);
            }

            return chatHistory;
        }

        /// <summary>
        /// 调用 Ollama 模型获取回复
        /// </summary>
        private async Task<string> CallLlmAsync(ChatHistory chatHistory)
        {
            try
            {
                var response = await _chatCompletion.GetChatMessageContentAsync(chatHistory);
                return response.Content ?? "抱歉，AI 返回了空响应，请重试。";
            }
            catch (Exception ex)
            {
                return $"AI 服务暂时不可用：{ex.Message}";
            }
        }
    }
}
