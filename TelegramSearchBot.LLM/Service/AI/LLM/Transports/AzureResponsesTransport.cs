using Microsoft.Extensions.Logging;
using TelegramSearchBot.Model.AI;
using TelegramSearchBot.Model.Data;

namespace TelegramSearchBot.Service.AI.LLM.Transports {
    /// <summary>
    /// Azure OpenAI Responses. The body matches <see cref="ResponsesTransport"/>; the base URL is normalized
    /// to <c>/openai/v1</c> and the SDK bearer token is copied to the <c>api-key</c> header.
    /// </summary>
    public static class AzureResponsesTransport {
        public static LlmTransportBundle Create(LLMChannel channel, LLMApiBinding binding, string modelName,
            bool promptCachingEnabled, bool supportsVision, ILogger logger, IHttpClientFactory httpClientFactory) {
            var endpoint = LlmPresetEndpoints.NormalizeAzure(LlmBindingSupport.ResolveEndpoint(channel, binding));
            var apiKey = LlmBindingSupport.ResolveApiKey(channel, binding);
            var bundle = ResponsesTransport.Create(channel, binding, modelName, endpoint, apiKey,
                promptCachingEnabled, supportsVision, logger, new AzureHeaderHttpClientFactory());
            bundle.Config.Protocol = LlmProtocol.AzureOpenAIResponses;
            bundle.Config.Endpoint = endpoint;
            return bundle;
        }
    }

    internal sealed class AzureHeaderHttpClientFactory : IHttpClientFactory {

        public HttpClient CreateClient(string name) {
            var handler = new AzureApiKeyHandler { InnerHandler = new HttpClientHandler() };
            return new HttpClient(handler, disposeHandler: true);
        }
    }

    internal sealed class AzureApiKeyHandler : DelegatingHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (request.Headers.Authorization?.Parameter is { Length: > 0 } token) {
                request.Headers.Remove("api-key");
                request.Headers.TryAddWithoutValidation("api-key", token);
                request.Headers.Authorization = null;
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
