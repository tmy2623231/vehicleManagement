using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace vehicleManagement.aiServer.BLL
{
    /// <summary>
    /// 中转网关路由适配：OpenAI SDK 固定把对话补全发到 {Endpoint}/chat/completions，
    /// 部分中转把该后缀改成了别的路径。这里在最后一跳把路径改写成 AiServer:ChatCompletionPath。
    /// 只有 ChatCompletionPath 与默认值不同时才会挂载本处理器。
    /// </summary>
    public sealed class ChatCompletionPathHandler : DelegatingHandler
    {
        private readonly string _path;

        public ChatCompletionPathHandler(string path)
        {
            _path = string.IsNullOrWhiteSpace(path) ? AiServerOptions.DefaultChatCompletionPath : path.Trim();
            InnerHandler = new HttpClientHandler();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is Uri uri)
            {
                var current = uri.AbsolutePath;
                var target = current.EndsWith(AiServerOptions.DefaultChatCompletionPath, StringComparison.OrdinalIgnoreCase)
                    ? current[..^AiServerOptions.DefaultChatCompletionPath.Length] + _path
                    : current.TrimEnd('/') + _path;

                request.RequestUri = new UriBuilder(uri) { Path = target }.Uri;
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
