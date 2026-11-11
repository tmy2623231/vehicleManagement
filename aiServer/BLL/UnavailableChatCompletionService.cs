using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

#nullable enable

namespace vehicleManagement.aiServer.BLL
{
    /// <summary>
    /// 缺 BaseUrl / ApiKey 时的占位连接器：注册进 Kernel 保证依赖注入链路不断，
    /// 真正调用时才抛出「未配置」异常，由 ChatService 分类成面向用户的降级文案。
    /// 这样 /ai-chat 页面与所有业务页面在 AI 未配置时照常工作。
    /// </summary>
    public sealed class UnavailableChatCompletionService : IChatCompletionService
    {
        public const string UnconfiguredMessage = "AI 服务未配置（缺少 AiServer:BaseUrl / ApiKey / ModelId）";

        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(UnconfiguredMessage);

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            // async 迭代器必须含 yield 才合法；枚举到第一项时由 ThrowStream 抛出未配置异常
            foreach (var chunk in ThrowStream())
                yield return chunk;
        }

        private static IEnumerable<StreamingChatMessageContent> ThrowStream()
            => throw new InvalidOperationException(UnconfiguredMessage);
    }
}
