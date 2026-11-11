using System;
using System.Collections.Generic;

#nullable enable

namespace vehicleManagement.aiServer.BLL
{
    /// <summary>
    /// AI 接入配置（全部集中在 aisettings.json 的 AiServer 节）。
    /// 取值优先级：环境变量（AiServer__BaseUrl 等）&gt; aisettings.json &gt; 本类默认值。
    /// </summary>
    public class AiServerOptions
    {
        public const string SectionName = "AiServer";
        public const string DefaultChatCompletionPath = "/chat/completions";
        public const int DefaultTimeoutSeconds = 60;

        /// <summary>连接器：OpenAI（默认，可指向任意 OpenAI 兼容中转）/ Ollama（回退备用，改配置即可切回）。</summary>
        public string Provider { get; set; } = "OpenAI";

        /// <summary>中转或官方基址，如 https://api.openai.com/v1 、https://your-gateway/v1 。必填。</summary>
        public string BaseUrl { get; set; } = "";

        /// <summary>密钥。必填（Ollama 除外）。禁止把真实值提交进仓库。</summary>
        public string ApiKey { get; set; } = "";

        /// <summary>模型标识，随中转实际支持的模型变化。必填。</summary>
        public string ModelId { get; set; } = "";

        /// <summary>对话补全路由后缀，兼容部分中转的路由差异。默认 /chat/completions。</summary>
        public string ChatCompletionPath { get; set; } = DefaultChatCompletionPath;

        /// <summary>单次请求超时秒数。默认 60。</summary>
        public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

        /// <summary>可选：单次回复最大 token 数；为空表示交给服务端默认。</summary>
        public int? MaxTokens { get; set; }

        /// <summary>可选：采样温度；为空表示交给服务端默认。</summary>
        public double? Temperature { get; set; }

        /// <summary>可选：是否启用模型自主 Text-to-SQL 副路径。默认 false（主路径行为与改动前完全一致）。</summary>
        public bool TextToSqlEnabled { get; set; }

        public bool IsOllama
            => string.Equals((Provider ?? "").Trim(), "Ollama", StringComparison.OrdinalIgnoreCase);

        public bool IsOpenAiCompatible => !IsOllama;

        public bool HasBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);

        public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

        public bool HasModelId => !string.IsNullOrWhiteSpace(ModelId);

        /// <summary>是否具备调用模型的最小配置；缺项不抛异常，由调用方降级。</summary>
        public bool IsConfigured => HasBaseUrl && HasModelId && (IsOllama || HasApiKey);

        /// <summary>基址去掉尾部斜杠，避免拼出 //chat/completions。</summary>
        public string NormalizedBaseUrl => (BaseUrl ?? "").Trim().TrimEnd('/');

        public int EffectiveTimeoutSeconds => TimeoutSeconds > 0 ? TimeoutSeconds : DefaultTimeoutSeconds;

        /// <summary>规整后的对话补全路径：保证以 / 开头，空值回退默认。</summary>
        public string ResolvedChatCompletionPath
        {
            get
            {
                var path = (ChatCompletionPath ?? "").Trim();
                if (path.Length == 0) return DefaultChatCompletionPath;
                return path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
            }
        }

        /// <summary>日志里只允许出现掩码：保留后 4 位，前段一律打码。</summary>
        public string MaskedApiKey
        {
            get
            {
                var key = (ApiKey ?? "").Trim();
                if (key.Length == 0) return "(未配置)";
                if (key.Length <= 4) return "****";
                return "****" + key.Substring(key.Length - 4);
            }
        }

        /// <summary>缺失的必填项（用于启动告警文案）。</summary>
        public IReadOnlyList<string> MissingKeys
        {
            get
            {
                var missing = new List<string>();
                if (!HasBaseUrl) missing.Add("BaseUrl");
                if (!HasModelId) missing.Add("ModelId");
                if (IsOpenAiCompatible && !HasApiKey) missing.Add("ApiKey");
                return missing;
            }
        }

        /// <summary>启动日志摘要：BaseUrl / ModelId / TextToSqlEnabled 可打印，ApiKey 只出现掩码。</summary>
        public string DescribeForLog()
            => $"Provider={Provider}，BaseUrl={NormalizedBaseUrl}，ModelId={ModelId}，"
               + $"ChatCompletionPath={ResolvedChatCompletionPath}，Timeout={EffectiveTimeoutSeconds}s，"
               + $"MaxTokens={(MaxTokens.HasValue ? MaxTokens.Value.ToString() : "空")}，"
               + $"Temperature={(Temperature.HasValue ? Temperature.Value.ToString("0.##") : "空")}，"
               + $"TextToSqlEnabled={TextToSqlEnabled}，ApiKey={MaskedApiKey}";
    }
}
