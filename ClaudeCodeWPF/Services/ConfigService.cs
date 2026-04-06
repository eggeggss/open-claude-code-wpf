using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;

namespace OpenClaudeCodeWPF.Services
{
    public class AzureNodeConfig
    {
        public string Name       { get; set; }
        public string Endpoint   { get; set; }
        public string ApiKey     { get; set; }
        public string Deployment { get; set; }
        public string ApiVersion { get; set; }
    }

    /// <summary>
    /// 讀取和管理 App.config 中的所有設定
    /// </summary>
    public class ConfigService
    {
        private static ConfigService _instance;
        public static ConfigService Instance => _instance ?? (_instance = new ConfigService());

        private readonly NameValueCollection _settings;

        private ConfigService()
        {
            _settings = ConfigurationManager.AppSettings;
        }

        private string Get(string key, string defaultValue = "")
        {
            return _settings[key] ?? defaultValue;
        }

        private bool GetBool(string key, bool defaultValue = false)
        {
            var val = _settings[key];
            if (string.IsNullOrEmpty(val)) return defaultValue;
            return val.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private int GetInt(string key, int defaultValue = 0)
        {
            var val = _settings[key];
            if (int.TryParse(val, out int result)) return result;
            return defaultValue;
        }

        private double GetDouble(string key, double defaultValue = 0)
        {
            var val = _settings[key];
            if (double.TryParse(val, out double result)) return result;
            return defaultValue;
        }

        // ===== 通用設定 =====
        public string DefaultProvider => Get("DefaultProvider", "OpenAI");
        public string DefaultModel => Get("DefaultModel", "gpt-5");
        public int MaxRetries => GetInt("MaxRetries", 3);
        public int TimeoutSeconds => GetInt("TimeoutSeconds", 600);

        // ===== Anthropic =====
        public string AnthropicApiKey => Get("Anthropic.ApiKey");
        public string AnthropicBaseUrl => Get("Anthropic.BaseUrl", "https://api.anthropic.com");
        public string AnthropicApiVersion => Get("Anthropic.ApiVersion", "2023-06-01");
        public string AnthropicDefaultModel => Get("Anthropic.DefaultModel", "claude-sonnet-4-20250514");

        // ===== OpenAI =====
        public string OpenAIApiKey => Get("OpenAI.ApiKey");
        public string OpenAIBaseUrl => Get("OpenAI.BaseUrl", "https://api.openai.com/v1");
        public string OpenAIDefaultModel => Get("OpenAI.DefaultModel", "gpt-5");

        // ===== Azure OpenAI =====
        public string AzureOpenAIApiKey => Get("AzureOpenAI.ApiKey");
        public string AzureOpenAIEndpoint => Get("AzureOpenAI.Endpoint");
        public string AzureOpenAIDeploymentName => Get("AzureOpenAI.DeploymentName");
        public string AzureOpenAIApiVersion => Get("AzureOpenAI.ApiVersion", "2024-02-01");

        // ===== Gemini =====
        public string GeminiApiKey => Get("Gemini.ApiKey");
        public string GeminiBaseUrl => Get("Gemini.BaseUrl", "https://generativelanguage.googleapis.com");
        public string GeminiDefaultModel => Get("Gemini.DefaultModel", "gemini-2.0-flash");

        // ===== Ollama =====
        public string OllamaBaseUrl => Get("Ollama.BaseUrl", "http://localhost:11434");
        public string OllamaDefaultModel => Get("Ollama.DefaultModel", "llama3");
        public bool OllamaUseOpenAICompat => GetBool("Ollama.UseOpenAICompat", true);

        // ===== MCP =====
        public string MCPConfigPath => Get("MCP.ConfigPath", "mcp-servers.json");

        // ===== Skills =====
        public string SkillsDirectory => Get("Skills.Directory", "skills/");

        // ===== 執行時期可修改（不持久化到 App.config）=====
        private bool? _streamingEnabledRuntime;
        public bool StreamingEnabled
        {
            get => _streamingEnabledRuntime ?? GetBool("StreamingEnabled", true);
            set => _streamingEnabledRuntime = value;
        }

        private double? _temperatureRuntime;
        public double Temperature
        {
            get => _temperatureRuntime ?? GetDouble("Temperature", 0.7);
            set => _temperatureRuntime = value;
        }

        private int? _maxTokensRuntime;
        public int MaxTokens
        {
            get => _maxTokensRuntime ?? GetInt("MaxTokens", 8192);
            set => _maxTokensRuntime = value;
        }

        private string _language;
        public string Language
        {
            get => _language ?? Get("Language", "zh-TW");
            set => _language = value;
        }

        // ===== 字體設定 =====
        private double? _chatFontSize;
        public double ChatFontSize
        {
            get => _chatFontSize ?? GetDouble("ChatFontSize", 17);
            set => _chatFontSize = value;
        }

        private string _chatFontFamily;
        public string ChatFontFamily
        {
            get => _chatFontFamily ?? Get("ChatFontFamily", "Comic Sans MS");
            set => _chatFontFamily = value;
        }

        private string _themeName;
        public string ThemeName
        {
            get => _themeName ?? Get("Theme", "ClaudeCode");
            set => _themeName = value;
        }

        private string _currentProvider;
        public string CurrentProvider
        {
            get => _currentProvider ?? DefaultProvider;
            set
            {
                if (_currentProvider != value)
                {
                    _currentProvider = value;
                    _currentModel = null;
                }
            }
        }

        private string _currentModel;
        public string CurrentModel
        {
            get => _currentModel ?? GetCurrentDefaultModel();
            set => _currentModel = value;
        }

        public bool CurrentModelSupportsVision { get; set; } = false;

        private string GetCurrentDefaultModel()
        {
            switch (CurrentProvider)
            {
                case "OpenAI":      return OpenAIDefaultModel;
                case "AzureOpenAI": return AzureOpenAIDeploymentName ?? "gpt-4o";
                case "Gemini":      return GeminiDefaultModel;
                case "Ollama":      return OllamaDefaultModel;
                default:            return AnthropicDefaultModel;
            }
        }

        // ===== 執行時期 per-provider API Key / Base URL overrides =====
        private readonly Dictionary<string, string> _apiKeyOverrides = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _apiBaseOverrides = new Dictionary<string, string>();

        public void SetApiKeyForProvider(string provider, string key)
        {
            if (key != null) _apiKeyOverrides[provider] = key;
        }

        public string GetApiKeyForProvider(string provider)
        {
            if (_apiKeyOverrides.TryGetValue(provider, out var key)) return key;
            switch (provider)
            {
                case "Anthropic":   return AnthropicApiKey;
                case "OpenAI":      return OpenAIApiKey;
                case "AzureOpenAI": return AzureOpenAIApiKey;
                case "Gemini":      return GeminiApiKey;
                default:            return "";
            }
        }

        public void SetBaseUrlForProvider(string provider, string url)
        {
            if (url != null) _apiBaseOverrides[provider] = url;
        }

        public string GetBaseUrlForProvider(string provider)
        {
            if (_apiBaseOverrides.TryGetValue(provider, out var url)) return url;
            switch (provider)
            {
                case "Anthropic": return AnthropicBaseUrl;
                case "OpenAI":    return OpenAIBaseUrl;
                case "Gemini":    return GeminiBaseUrl;
                case "Ollama":    return OllamaBaseUrl;
                default:          return "";
            }
        }

        // Backward-compat helpers
        public string GetCurrentApiKey()  => GetApiKeyForProvider(CurrentProvider);
        public void   SetCurrentApiKey(string key)  => SetApiKeyForProvider(CurrentProvider, key);
        public string GetCurrentApiBase() => GetBaseUrlForProvider(CurrentProvider);
        public void   SetCurrentApiBase(string url) => SetBaseUrlForProvider(CurrentProvider, url);

        // ===== Ollama 自訂模型清單（換行分隔） =====
        private string _ollamaModels;
        public string OllamaModels
        {
            get => _ollamaModels ?? "";
            set => _ollamaModels = value;
        }

        // ===== Azure OpenAI 多節點文字（每行：名稱|Endpoint|ApiKey|DeploymentName|ApiVersion） =====
        private string _azureOpenAINodes;
        public string AzureOpenAINodes
        {
            get => _azureOpenAINodes ?? "";
            set => _azureOpenAINodes = value;
        }

        /// <summary>解析多節點文字，回傳結構化清單</summary>
        public List<AzureNodeConfig> GetParsedAzureNodes()
        {
            var result = new List<AzureNodeConfig>();
            foreach (var raw in AzureOpenAINodes.Split('\n'))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split('|');
                if (parts.Length < 4) continue;
                result.Add(new AzureNodeConfig
                {
                    Name       = parts[0].Trim(),
                    Endpoint   = parts[1].Trim(),
                    ApiKey     = parts.Length > 2 ? parts[2].Trim() : "",
                    Deployment = parts[3].Trim(),
                    ApiVersion = parts.Length > 4 ? parts[4].Trim() : "2024-02-01"
                });
            }
            // Fallback: legacy single-node from App.config
            if (result.Count == 0
                && !string.IsNullOrEmpty(AzureOpenAIEndpoint)
                && !string.IsNullOrEmpty(AzureOpenAIDeploymentName))
            {
                result.Add(new AzureNodeConfig
                {
                    Name       = AzureOpenAIDeploymentName,
                    Endpoint   = AzureOpenAIEndpoint,
                    ApiKey     = AzureOpenAIApiKey,
                    Deployment = AzureOpenAIDeploymentName,
                    ApiVersion = AzureOpenAIApiVersion
                });
            }
            return result;
        }
    }
}
