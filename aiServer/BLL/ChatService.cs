using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using vehicleManagement.aiServer.BLL.Enums;
using vehicleManagement.aiServer.BLL.Prompts;
using vehicleManagement.aiServer.BLL.TextToSql;
using vehicleManagement.aiServer.DAL.Model;
using vehicleManagement.aiServer.DAL.Repository;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Logging;
using vehicleManagement.DAL.Common;

#nullable enable

namespace vehicleManagement.aiServer.BLL
{
    /// <summary>
    /// AI 对话服务实现：Semantic Kernel（OpenAI 兼容连接器）+ 意图识别 + 数据库查询 + 记忆管理。
    /// 取数主路径：规则意图 → 白名单查询 → 注入上下文 → 模型归纳；
    /// 副路径（可开关）：模型自主 Text-to-SQL，失败静默回退主路径。
    /// </summary>
    public class ChatService
    {
        private readonly ChatSessionRepository _sessionRepo;
        private readonly ChatMessageRepository _messageRepo;
        private readonly MemoryService _memoryService;
        private readonly DataQuerier _dataQuerier;
        private readonly TextToSqlService _textToSql;
        private readonly AiServerOptions _aiOptions;
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chatCompletion;
        private readonly ILogger<ChatService> _logger;

        private const int MaxRecentMessages = 20;

        public ChatService(
            ChatSessionRepository sessionRepo,
            ChatMessageRepository messageRepo,
            MemoryService memoryService,
            DataQuerier dataQuerier,
            TextToSqlService textToSql,
            AiServerOptions aiOptions,
            Kernel kernel,
            ILogger<ChatService> logger)
        {
            _sessionRepo = sessionRepo;
            _messageRepo = messageRepo;
            _memoryService = memoryService;
            _dataQuerier = dataQuerier;
            _textToSql = textToSql;
            _aiOptions = aiOptions;
            _kernel = kernel;
            _logger = logger;
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
                CreatedAt = ChinaTime.Now,
                UpdatedAt = ChinaTime.Now
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

        /// <summary>发送消息并取 AI 回复（存消息 → 意图识别 → 查库 → LLM → 存回复）。</summary>
        public async Task<ChatMessage> SendMessageAsync(int sessionId, string content)
        {
            // 1. 保存用户消息
            var userMessage = new ChatMessage
            {
                SessionId = sessionId,
                Role = "user",
                Content = content,
                CreatedAt = ChinaTime.Now
            };
            await _messageRepo.AddAsync(userMessage);

            // 自动更新会话标题
            await UpdateSessionMetadataAsync(sessionId, content);

            // 2. 意图识别（规则词表，主路径口径不变）
            var (intent, keyword) = IntentDetector.Detect(content);

            // 3. 主路径：白名单查询（模型不写 SQL）
            var dataContext = await _dataQuerier.QueryAsync(intent, keyword);

            // 3b. 副路径：规则词表未命中或白名单查询没拿到数据时，允许模型自主 Text-to-SQL 补一刀。
            //     开关关闭、校验不通过、执行报错一律静默回退，页面不会看到 SQL 异常。
            if (_aiOptions.TextToSqlEnabled && !HasDataRows(dataContext))
            {
                var sqlContext = await _textToSql.TryQueryAsync(content);
                if (!string.IsNullOrWhiteSpace(sqlContext))
                    dataContext = dataContext is null ? sqlContext : dataContext + sqlContext;
            }

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
                CreatedAt = ChinaTime.Now
            };
            await _messageRepo.AddAsync(assistantMessage);

            // 7. 提取记忆：本地正则 + 几次小写入，直接 await（原 Task.Run 会拿到已释放的 Scoped 服务）
            await _memoryService.ExtractAndStoreAsync(content, assistantContent, sessionId);

            return assistantMessage;
        }

        // ==================== 私有辅助方法 ====================

        /// <summary>
        /// 主路径是否已经拿到数据行：DataQuerier 的每条数据行都以「- 」开头。
        /// 用于判断是否需要走 Text-to-SQL 副路径。
        /// </summary>
        private static bool HasDataRows(string? dataContext)
            => !string.IsNullOrWhiteSpace(dataContext) && dataContext.Contains("- ", StringComparison.Ordinal);

        /// <summary>
        /// 更新会话的标题和更新时间
        /// </summary>
        private async Task UpdateSessionMetadataAsync(int sessionId, string content)
        {
            var session = await _sessionRepo.GetByIdAsync(sessionId);
            if (session == null) return;

            if (session.Title == "新对话" || string.IsNullOrEmpty(session.Title))
                session.Title = content.Length > 30 ? content[..30] + "..." : content;

            session.UpdatedAt = ChinaTime.Now;
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
        /// 调用模型获取回复（超时来自 AiServer:TimeoutSeconds，输出长度/温度来自配置的可选项）
        /// </summary>
        private async Task<string> CallLlmAsync(ChatHistory chatHistory)
        {
            var timeoutSeconds = _aiOptions.EffectiveTimeoutSeconds;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                var response = await _chatCompletion.GetChatMessageContentAsync(
                    chatHistory, BuildExecutionSettings(), _kernel, cts.Token);
                return response.Content ?? "抱歉，AI 返回了空响应，请重试。";
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("AI 推理超时（{Seconds}s）", timeoutSeconds);
                return "AI 服务响应超时，请稍后重试。";
            }
            catch (Exception ex)
            {
                // 面向用户的文案不带 ex.Message：里面会带出主机名、端口、内部路径
                _logger.LogError(ex, "AI 对话调用失败");
                return "AI 服务暂时不可用，请稍后重试或联系管理员。";
            }
        }

        /// <summary>
        /// 仅 OpenAI 兼容供应商下发 MaxTokens / Temperature；未配置则交给服务端默认。
        /// </summary>
        private OpenAIPromptExecutionSettings? BuildExecutionSettings()
        {
            if (!_aiOptions.IsOpenAiCompatible) return null;
            if (!_aiOptions.MaxTokens.HasValue && !_aiOptions.Temperature.HasValue) return null;

            return new OpenAIPromptExecutionSettings
            {
                MaxTokens = _aiOptions.MaxTokens,
                Temperature = _aiOptions.Temperature
            };
        }
    }
}
