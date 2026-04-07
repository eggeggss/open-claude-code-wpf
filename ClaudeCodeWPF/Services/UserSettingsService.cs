using System;
using System.IO;
using Newtonsoft.Json;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 持久化用戶偏好設定至 %AppData%\OpenClaudeCodeWPF\usersettings.json
    /// </summary>
    public class UserSettingsService
    {
        private static UserSettingsService _instance;
        public static UserSettingsService Instance => _instance ?? (_instance = new UserSettingsService());

        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenClaudeCodeWPF");
        private static readonly string SettingsFile = Path.Combine(SettingsDir, "usersettings.json");

        public class UserSettings
        {
            public string CurrentProvider  { get; set; } = "OpenAI";
            public string CurrentModel     { get; set; } = "gpt-5";
            public double ChatFontSize     { get; set; } = 17;
            public string ChatFontFamily   { get; set; } = "Comic Sans MS";
            public double Temperature      { get; set; } = 0.7;
            public int    MaxTokens        { get; set; } = 8192;
            public bool   StreamingEnabled { get; set; } = true;
            public string Language         { get; set; } = "zh-TW";
            public string ThemeName        { get; set; } = "ClaudeCode";
            // Per-provider API keys
            public string AnthropicApiKey  { get; set; } = "";
            public string GeminiApiKey     { get; set; } = "";
            public string OpenRouterApiKey { get; set; } = "";
            public string OpenAIApiKey     { get; set; } = "";
            public string OpenAIBaseUrl    { get; set; } = "";
            // Ollama
            public string OllamaBaseUrl    { get; set; } = "http://localhost:11434";
            public string OllamaModels     { get; set; } = "";
            // Azure OpenAI multi-node (each line: Name|Endpoint|ApiKey|Deployment|ApiVersion)
            public string AzureOpenAINodes { get; set; } = "";
            // UI layout state
            public bool   SidebarCollapsed { get; set; } = true;
            public double SidebarWidth     { get; set; } = 240;
        }

        private UserSettings _settings;

        private UserSettingsService()
        {
            _settings = Load();
        }

        // ── Public getters/setters ──────────────────────────────────────────

        public string CurrentProvider
        {
            get => _settings.CurrentProvider;
            set { _settings.CurrentProvider = value; }
        }

        public string CurrentModel
        {
            get => _settings.CurrentModel;
            set { _settings.CurrentModel = value; }
        }

        public double ChatFontSize
        {
            get => _settings.ChatFontSize;
            set { _settings.ChatFontSize = value; }
        }

        public string ChatFontFamily
        {
            get => _settings.ChatFontFamily;
            set { _settings.ChatFontFamily = value; }
        }

        public double Temperature
        {
            get => _settings.Temperature;
            set { _settings.Temperature = value; }
        }

        public int MaxTokens
        {
            get => _settings.MaxTokens;
            set { _settings.MaxTokens = value; }
        }

        public bool StreamingEnabled
        {
            get => _settings.StreamingEnabled;
            set { _settings.StreamingEnabled = value; }
        }

        public string Language
        {
            get => _settings.Language;
            set { _settings.Language = value; }
        }

        public string ThemeName
        {
            get => _settings.ThemeName;
            set { _settings.ThemeName = value; }
        }

        public string AzureOpenAINodes
        {
            get => _settings.AzureOpenAINodes;
            set { _settings.AzureOpenAINodes = value; }
        }

        public bool SidebarCollapsed
        {
            get => _settings.SidebarCollapsed;
            set { _settings.SidebarCollapsed = value; }
        }

        public double SidebarWidth
        {
            get => _settings.SidebarWidth;
            set { _settings.SidebarWidth = value; }
        }

        // ── Persistence ─────────────────────────────────────────────────────

        private UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var s = JsonConvert.DeserializeObject<UserSettings>(json);
                    if (s != null) return s;
                }
            }
            catch { /* ignore corrupt settings */ }
            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(_settings, Formatting.Indented));
            }
            catch { /* ignore save failures */ }
        }

        /// <summary>從已儲存的設定套用到 ConfigService（啟動時呼叫）</summary>
        public void ApplyToConfig()
        {
            var cfg = ConfigService.Instance;
            cfg.CurrentProvider   = _settings.CurrentProvider;
            cfg.CurrentModel      = _settings.CurrentModel;
            cfg.ChatFontSize      = _settings.ChatFontSize;
            cfg.ChatFontFamily    = _settings.ChatFontFamily;
            cfg.Temperature       = _settings.Temperature;
            cfg.MaxTokens         = _settings.MaxTokens;
            cfg.StreamingEnabled  = _settings.StreamingEnabled;
            cfg.Language          = _settings.Language;
            cfg.ThemeName         = _settings.ThemeName;
            // Per-provider API keys
            if (!string.IsNullOrEmpty(_settings.AnthropicApiKey))
                cfg.SetApiKeyForProvider("Anthropic", _settings.AnthropicApiKey);
            if (!string.IsNullOrEmpty(_settings.GeminiApiKey))
                cfg.SetApiKeyForProvider("Gemini", _settings.GeminiApiKey);
            if (!string.IsNullOrEmpty(_settings.OpenRouterApiKey))
                cfg.SetApiKeyForProvider("OpenRouter", _settings.OpenRouterApiKey);
            if (!string.IsNullOrEmpty(_settings.OpenAIApiKey))
                cfg.SetApiKeyForProvider("OpenAI", _settings.OpenAIApiKey);
            if (!string.IsNullOrEmpty(_settings.OpenAIBaseUrl))
                cfg.SetBaseUrlForProvider("OpenAI", _settings.OpenAIBaseUrl);
            cfg.SetBaseUrlForProvider("Ollama", _settings.OllamaBaseUrl);
            cfg.OllamaModels      = _settings.OllamaModels;
            cfg.AzureOpenAINodes  = _settings.AzureOpenAINodes;
        }

        /// <summary>從 ConfigService 快照目前設定（儲存前呼叫）</summary>
        public void SnapshotFromConfig()
        {
            var cfg = ConfigService.Instance;
            _settings.CurrentProvider  = cfg.CurrentProvider;
            _settings.CurrentModel     = cfg.CurrentModel;
            _settings.ChatFontSize     = cfg.ChatFontSize;
            _settings.ChatFontFamily   = cfg.ChatFontFamily;
            _settings.Temperature      = cfg.Temperature;
            _settings.MaxTokens        = cfg.MaxTokens;
            _settings.StreamingEnabled = cfg.StreamingEnabled;
            _settings.Language         = cfg.Language;
            _settings.ThemeName        = cfg.ThemeName;
            // Per-provider API keys
            _settings.AnthropicApiKey  = cfg.GetApiKeyForProvider("Anthropic");
            _settings.GeminiApiKey     = cfg.GetApiKeyForProvider("Gemini");
            _settings.OpenRouterApiKey = cfg.GetApiKeyForProvider("OpenRouter");
            _settings.OpenAIApiKey     = cfg.GetApiKeyForProvider("OpenAI");
            _settings.OpenAIBaseUrl    = cfg.GetBaseUrlForProvider("OpenAI");
            _settings.OllamaBaseUrl    = cfg.GetBaseUrlForProvider("Ollama");
            _settings.OllamaModels     = cfg.OllamaModels;
            _settings.AzureOpenAINodes = cfg.AzureOpenAINodes;
        }
    }
}
