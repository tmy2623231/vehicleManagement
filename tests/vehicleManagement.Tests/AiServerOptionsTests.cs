using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.ChatCompletion;
using vehicleManagement.aiServer.BLL;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>
    /// AI 配置解析与缺配置降级：缺 BaseUrl / ApiKey 时不得抛未捕获异常，
    /// 且日志里不得出现完整 ApiKey。
    /// </summary>
    public class AiServerOptionsTests
    {
        private static AiServerOptions BindFrom(params (string Key, string Value)[] settings)
        {
            var values = new Dictionary<string, string>();
            foreach (var (key, value) in settings)
                values[key] = value;

            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            var options = new AiServerOptions();
            config.GetSection(AiServerOptions.SectionName).Bind(options);
            return options;
        }

        [Fact]
        public void MissingApiKey_IsNotConfiguredAndReportsMissingKey()
        {
            var options = BindFrom(
                ("AiServer:BaseUrl", "https://gw.example/v1"),
                ("AiServer:ModelId", "gpt-4o-mini"));

            Assert.False(options.IsConfigured);
            Assert.Contains("ApiKey", options.MissingKeys);
            Assert.DoesNotContain("BaseUrl", options.MissingKeys);
        }

        [Fact]
        public void MissingBaseUrl_IsNotConfiguredAndReportsMissingKey()
        {
            var options = BindFrom(
                ("AiServer:ApiKey", "sk-test-key-1234"),
                ("AiServer:ModelId", "gpt-4o-mini"));

            Assert.False(options.IsConfigured);
            Assert.Contains("BaseUrl", options.MissingKeys);
        }

        [Fact]
        public void EmptyOptions_DefaultsAreSafeAndDoNotThrow()
        {
            var options = new AiServerOptions();

            Assert.False(options.IsConfigured);
            Assert.Equal("OpenAI", options.Provider);
            Assert.Equal("/chat/completions", options.ResolvedChatCompletionPath);
            Assert.Equal(60, options.EffectiveTimeoutSeconds);
            Assert.False(options.TextToSqlEnabled);
            Assert.Equal(3, options.MissingKeys.Count);
        }

        [Fact]
        public void FullConfiguration_IsConfiguredAndNeverLogsPlainApiKey()
        {
            var options = BindFrom(
                ("AiServer:BaseUrl", "https://gw.example/v1/"),
                ("AiServer:ApiKey", "sk-live-abcdefgh5678"),
                ("AiServer:ModelId", "qwen-max"),
                ("AiServer:TextToSqlEnabled", "true"),
                ("AiServer:TimeoutSeconds", "30"));

            Assert.True(options.IsConfigured);
            Assert.Equal("https://gw.example/v1", options.NormalizedBaseUrl);
            Assert.True(options.TextToSqlEnabled);
            Assert.Equal(30, options.EffectiveTimeoutSeconds);

            var summary = options.DescribeForLog();
            Assert.DoesNotContain("sk-live-abcdefgh5678", summary);
            Assert.Contains("5678", summary);          // 只保留后 4 位
            Assert.Contains("qwen-max", summary);
            Assert.Contains("TextToSqlEnabled=True", summary);
        }

        [Fact]
        public void ShortApiKey_IsFullyMasked()
        {
            var options = new AiServerOptions { ApiKey = "ab" };
            Assert.Equal("****", options.MaskedApiKey);
        }

        [Fact]
        public void ChatCompletionPath_IsNormalizedToLeadingSlash()
        {
            var options = new AiServerOptions { ChatCompletionPath = "chat/completions" };
            Assert.Equal("/chat/completions", options.ResolvedChatCompletionPath);

            options.ChatCompletionPath = "  ";
            Assert.Equal(AiServerOptions.DefaultChatCompletionPath, options.ResolvedChatCompletionPath);
        }

        [Fact]
        public async Task UnconfiguredPlaceholder_ThrowsOnlyOnCall_SoDiStaysHealthy()
        {
            var placeholder = new UnavailableChatCompletionService();

            // 构造（DI 解析）阶段不得抛异常，否则 /ai-chat 整页挂掉
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => placeholder.GetChatMessageContentsAsync(new ChatHistory()));
            Assert.Contains("未配置", ex.Message);
        }
    }
}
