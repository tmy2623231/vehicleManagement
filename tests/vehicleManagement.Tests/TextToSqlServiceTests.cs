using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using vehicleManagement.aiServer.BLL;
using vehicleManagement.aiServer.BLL.TextToSql;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>
    /// Text-to-SQL 副路径的降级：开关关闭、SQL 越界、模型报错都必须静默返回 null，
    /// 由调用方（ChatService）继续走主路径，页面不报错。
    /// </summary>
    public class TextToSqlServiceTests
    {
        private readonly TestDbFactory _dbFactory = new();

        private sealed class ScriptedChatCompletion : IChatCompletionService
        {
            private readonly string _reply;
            private readonly System.Exception _throw;

            public ScriptedChatCompletion(string reply) => _reply = reply;

            public ScriptedChatCompletion(System.Exception toThrow) => _throw = toThrow;

            public IReadOnlyDictionary<string, object> Attributes { get; } = new Dictionary<string, object>();

            public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
                ChatHistory chatHistory, PromptExecutionSettings executionSettings = null,
                Kernel kernel = null, CancellationToken cancellationToken = default)
            {
                if (_throw != null) throw _throw;
                IReadOnlyList<ChatMessageContent> result = new[]
                {
                    new ChatMessageContent(AuthorRole.Assistant, _reply)
                };
                return Task.FromResult(result);
            }

            public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
                ChatHistory chatHistory, PromptExecutionSettings executionSettings = null,
                Kernel kernel = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask;
                yield return new StreamingChatMessageContent(AuthorRole.Assistant, _reply);
            }
        }

        private static AiServerOptions Options(bool textToSqlEnabled) => new()
        {
            Provider = "OpenAI",
            BaseUrl = "https://gw.example/v1",
            ApiKey = "sk-test-key",
            ModelId = "gpt-4o-mini",
            TextToSqlEnabled = textToSqlEnabled
        };

        private TextToSqlService Build(IChatCompletionService completion, bool textToSqlEnabled)
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(completion);
            var kernel = builder.Build();

            return new TextToSqlService(
                _dbFactory, kernel, Options(textToSqlEnabled), NullLogger<TextToSqlService>.Instance);
        }

        [Fact]
        public async Task SwitchOff_ReturnsNull_EvenIfModelWouldAnswer()
        {
            var service = Build(new ScriptedChatCompletion("SELECT LicensePlate FROM Vehicles LIMIT 5"), false);

            Assert.Null(await service.TryQueryAsync("哪些车？"));
        }

        [Fact]
        public async Task NonSelectSql_FallsBackToNull()
        {
            var service = Build(new ScriptedChatCompletion("DELETE FROM Vehicles"), true);

            Assert.Null(await service.TryQueryAsync("把车都删了"));
        }

        [Fact]
        public async Task OutOfWhitelistSql_FallsBackToNull()
        {
            var service = Build(new ScriptedChatCompletion("SELECT * FROM AppUsers"), true);

            Assert.Null(await service.TryQueryAsync("看看账号"));
        }

        [Fact]
        public async Task ModelFailure_FallsBackToNull()
        {
            var service = Build(new ScriptedChatCompletion(new System.InvalidOperationException("gateway 502")), true);

            Assert.Null(await service.TryQueryAsync("本月谁用得最多"));
        }
    }
}
