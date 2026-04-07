using System;
using System.Collections.Generic;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.Providers;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 根據設定建立對應的模型供應商實例
    /// </summary>
    public class ModelProviderFactory
    {
        private static ModelProviderFactory _instance;
        public static ModelProviderFactory Instance => _instance ?? (_instance = new ModelProviderFactory());

        private readonly Dictionary<string, IModelProvider> _providers;

        private ModelProviderFactory()
        {
            _providers = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase)
            {
                { "Anthropic",   new AnthropicProvider() },
                { "OpenAI",      new OpenAIProvider() },
                { "AzureOpenAI", new AzureOpenAIProvider() },
                { "Gemini",      new GeminiProvider() },
                { "Ollama",      new OllamaProvider() },
                { "OpenRouter",  new OpenRouterProvider() },
            };
        }

        public IModelProvider GetProvider(string providerName)
        {
            if (string.IsNullOrEmpty(providerName))
                providerName = ConfigService.Instance.DefaultProvider;

            if (_providers.TryGetValue(providerName, out var provider))
                return provider;

            throw new Exception($"Unknown provider: {providerName}");
        }

        public IModelProvider GetCurrentProvider()
        {
            return GetProvider(ConfigService.Instance.CurrentProvider);
        }

        public List<IModelProvider> GetAllProviders()
        {
            return new List<IModelProvider>(_providers.Values);
        }

        public List<IModelProvider> GetAvailableProviders()
        {
            var result = new List<IModelProvider>();
            foreach (var p in _providers.Values)
            {
                if (p.IsAvailable) result.Add(p);
            }
            return result;
        }
    }
}
