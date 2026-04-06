using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.Providers
{
    /// <summary>
    /// Azure OpenAI 多節點供應商。
    /// 每個節點對應一個 ModelInfo；ChatAsync 依 CurrentModel 查找正確節點。
    /// POST https://{endpoint}/openai/deployments/{deployment}/chat/completions?api-version=…
    /// </summary>
    public class AzureOpenAIProvider : OpenAIProvider
    {
        public override string ProviderName => "AzureOpenAI";

        public override bool IsAvailable => _config.GetParsedAzureNodes().Count > 0;

        // These are used by the base OpenAIProvider; we override them per-request
        protected override string ApiKey    => GetCurrentNode()?.ApiKey     ?? "";
        protected override string BaseUrl   => BuildBaseUrl(GetCurrentNode());
        protected override string DefaultModel => GetCurrentNode()?.Deployment ?? "";

        private AzureNodeConfig GetCurrentNode()
        {
            var nodes = _config.GetParsedAzureNodes();
            if (nodes.Count == 0) return null;
            var current = _config.CurrentModel;
            return nodes.FirstOrDefault(n => n.Name == current) ?? nodes[0];
        }

        private string BuildBaseUrl(AzureNodeConfig node)
        {
            if (node == null) return "";
            var ep = node.Endpoint.TrimEnd('/');
            return $"{ep}/openai/deployments/{node.Deployment}?api-version={node.ApiVersion}";
        }

        protected override HttpClient CreateClient()
        {
            var node   = GetCurrentNode();
            var client = Utils.HttpClientFactory.Create(_config.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("api-key", node?.ApiKey ?? "");
            return client;
        }

        protected override JObject BuildRequestBody(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            List<ToolDefinition> tools,
            bool stream)
        {
            var body = base.BuildRequestBody(messages, systemPrompt, parameters, tools, stream);
            body.Remove("model"); // deployment is in the URL, not the body
            return body;
        }

        public override Task<List<ModelInfo>> GetAvailableModelsAsync()
        {
            var result = _config.GetParsedAzureNodes()
                .Select(n => new ModelInfo
                {
                    Id           = n.Name,
                    DisplayName  = $"{n.Name} ({n.Deployment})",
                    Provider     = ProviderName,
                    MaxTokens    = 16384,
                    SupportsTools = true
                })
                .ToList();

            if (result.Count == 0)
                result.Add(new ModelInfo
                {
                    Id = "(未設定)", DisplayName = "Azure OpenAI（未設定節點）",
                    Provider = ProviderName, MaxTokens = 16384
                });

            return Task.FromResult(result);
        }
    }
}
