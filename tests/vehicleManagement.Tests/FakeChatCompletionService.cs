using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace vehicleManagement.Tests
{
    /// <summary>假 LLM（固定回复），让 ChatService 全链路在无 Ollama 环境下可测。</summary>
    internal sealed class FakeChatCompletionService : IChatCompletionService
    {
        public const string FixedReply = "好的，已记录。";

        private readonly System.Exception _throw;

        public FakeChatCompletionService() { }

        /// <summary>构造一个必定抛指定异常的实例，用于验证超时/故障分支。</summary>
        public FakeChatCompletionService(System.Exception toThrow) => _throw = toThrow;

        // 测试工程是 Nullable=disable，这里不写 object? 以免 CS8632
        public IReadOnlyDictionary<string, object> Attributes { get; } = new Dictionary<string, object>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings executionSettings = null,
            Kernel kernel = null,
            CancellationToken cancellationToken = default)
        {
            if (_throw != null) throw _throw;
            IReadOnlyList<ChatMessageContent> result = new[]
            {
                new ChatMessageContent(AuthorRole.Assistant, FixedReply)
            };
            return Task.FromResult(result);
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings executionSettings = null,
            Kernel kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (_throw != null) throw _throw;
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, FixedReply);
            await Task.CompletedTask;
        }
    }
}
