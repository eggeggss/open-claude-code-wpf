using System.Collections.Generic;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services.Providers
{
    /// <summary>
    /// Ollama 本地模型供應商 (OpenAI-compatible endpoint)
    /// POST http://localhost:11434/v1/chat/completions  (OpenAI compat mode)
    /// POST http://localhost:11434/api/chat            (native mode)
    /// </summary>
    public class OllamaProvider : OpenAIProvider
    {
        public override string ProviderName => "Ollama";
        public override bool IsAvailable => !string.IsNullOrEmpty(_config.OllamaBaseUrl);

        protected override string ApiKey => "ollama"; // Ollama doesn't need real API key
        protected override string BaseUrl => _config.OllamaUseOpenAICompat
            ? _config.OllamaBaseUrl.TrimEnd('/') + "/v1"
            : _config.OllamaBaseUrl;
        protected override string DefaultModel => _config.OllamaDefaultModel;
        protected override bool UseMaxCompletionTokens => false; // Ollama uses 'max_tokens'

        protected override System.Net.Http.HttpClient CreateClient()
        {
            var client = Utils.HttpClientFactory.Create(_config.TimeoutSeconds);
            // Ollama doesn't require auth header, but we set a dummy one for compat
            return client;
        }

        public override async Task<List<ModelInfo>> GetAvailableModelsAsync()
        {
            // 1. If user provided a custom model list, use it without contacting Ollama
            var customList = _config.OllamaModels?.Trim();
            if (!string.IsNullOrEmpty(customList))
                return ParseCustomModels(customList);

            // 2. Try fetching from the running Ollama server
            try
            {
                var client = Utils.HttpClientFactory.Create(10);
                var response = await client.GetAsync($"{_config.OllamaBaseUrl}/api/tags");
                if (!response.IsSuccessStatusCode) return GetDefaultModels();

                var json = await response.Content.ReadAsStringAsync();
                var obj  = Newtonsoft.Json.Linq.JObject.Parse(json);
                var models = obj["models"] as Newtonsoft.Json.Linq.JArray;

                var result = new List<ModelInfo>();
                if (models != null)
                {
                    foreach (var m in models)
                    {
                        var name = m["name"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                            result.Add(new ModelInfo
                            {
                                Id = name, DisplayName = name,
                                Provider = ProviderName, MaxTokens = 8192,
                                SupportsTools = true,
                                SupportsVision = IsVisionModel(name)
                            });
                    }
                }
                return result.Count > 0 ? result : GetDefaultModels();
            }
            catch
            {
                return GetDefaultModels();
            }
        }

        private List<ModelInfo> ParseCustomModels(string text)
        {
            var result = new List<ModelInfo>();
            foreach (var raw in text.Split('\n'))
            {
                var id = raw.Trim();
                if (string.IsNullOrEmpty(id)) continue;
                result.Add(new ModelInfo
                {
                    Id = id, DisplayName = id,
                    Provider = ProviderName, MaxTokens = 8192,
                    SupportsTools = true,
                    SupportsVision = IsVisionModel(id)
                });
            }
            return result.Count > 0 ? result : GetDefaultModels();
        }

        private List<ModelInfo> GetDefaultModels()
        {
            return new List<ModelInfo>
            {
                new ModelInfo { Id = "llama3",         DisplayName = "Llama 3",         Provider = ProviderName, MaxTokens = 8192, SupportsTools = true },
                new ModelInfo { Id = "llama3:70b",     DisplayName = "Llama 3 70B",     Provider = ProviderName, MaxTokens = 8192, SupportsTools = true },
                new ModelInfo { Id = "codellama",      DisplayName = "Code Llama",      Provider = ProviderName, MaxTokens = 4096, SupportsTools = false },
                new ModelInfo { Id = "mistral",        DisplayName = "Mistral",         Provider = ProviderName, MaxTokens = 8192, SupportsTools = true },
                new ModelInfo { Id = "qwen2.5-coder",  DisplayName = "Qwen2.5 Coder",  Provider = ProviderName, MaxTokens = 8192, SupportsTools = true },
                new ModelInfo { Id = "llava",          DisplayName = "LLaVA",           Provider = ProviderName, MaxTokens = 4096, SupportsTools = false, SupportsVision = true },
                new ModelInfo { Id = "llava:13b",      DisplayName = "LLaVA 13B",       Provider = ProviderName, MaxTokens = 4096, SupportsTools = false, SupportsVision = true },
            };
        }

        /// <summary>Heuristic: known Ollama vision model name patterns.</summary>
        private static bool IsVisionModel(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var lower = id.ToLowerInvariant();
            return lower.Contains("llava") || lower.Contains("vision") ||
                   lower.Contains("minicpm") || lower.Contains("bakllava") ||
                   lower.Contains("moondream") || lower.Contains("cogvlm") ||
                   lower.Contains("instructblip");
        }
    }
}
