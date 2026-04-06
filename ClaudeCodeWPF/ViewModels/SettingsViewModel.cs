using System;
using OpenClaudeCodeWPF.Services;

namespace OpenClaudeCodeWPF.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        // ── Provider API Keys ─────────────────────────────────────────────
        private string _anthropicKey;
        public string AnthropicKey
        {
            get => _anthropicKey;
            set => Set(ref _anthropicKey, value);
        }

        private string _openAIKey;
        public string OpenAIKey
        {
            get => _openAIKey;
            set => Set(ref _openAIKey, value);
        }

        private string _openAIBase;
        public string OpenAIBase
        {
            get => _openAIBase;
            set => Set(ref _openAIBase, value);
        }

        private string _geminiKey;
        public string GeminiKey
        {
            get => _geminiKey;
            set => Set(ref _geminiKey, value);
        }

        private string _ollamaBase;
        public string OllamaBase
        {
            get => _ollamaBase;
            set => Set(ref _ollamaBase, value);
        }

        private string _ollamaModels;
        public string OllamaModels
        {
            get => _ollamaModels;
            set => Set(ref _ollamaModels, value);
        }

        private string _azureNodes;
        public string AzureNodes
        {
            get => _azureNodes;
            set => Set(ref _azureNodes, value);
        }

        // ── Model Parameters ──────────────────────────────────────────────
        private double _temperature;
        public double Temperature
        {
            get => _temperature;
            set
            {
                Set(ref _temperature, value);
                OnPropertyChanged(nameof(TemperatureDisplay));
            }
        }
        public string TemperatureDisplay => _temperature.ToString("F1");

        private string _maxTokensText;
        public string MaxTokensText
        {
            get => _maxTokensText;
            set => Set(ref _maxTokensText, value);
        }

        private bool _streamingEnabled;
        public bool StreamingEnabled
        {
            get => _streamingEnabled;
            set => Set(ref _streamingEnabled, value);
        }

        private string _language;
        public string Language
        {
            get => _language;
            set => Set(ref _language, value);
        }

        // ── Font ──────────────────────────────────────────────────────────
        private double _fontSize;
        public double FontSize
        {
            get => _fontSize;
            set
            {
                Set(ref _fontSize, value);
                OnPropertyChanged(nameof(FontSizeDisplay));
            }
        }
        public string FontSizeDisplay => _fontSize.ToString("F0");

        private string _fontFamily;
        public string FontFamily
        {
            get => _fontFamily;
            set => Set(ref _fontFamily, value);
        }

        // ── System Prompt ─────────────────────────────────────────────────
        private string _systemPrompt;
        public string SystemPrompt
        {
            get => _systemPrompt;
            set => Set(ref _systemPrompt, value);
        }

        // ── Theme ─────────────────────────────────────────────────────────
        private string _currentTheme;
        public string CurrentTheme
        {
            get => _currentTheme;
            set => Set(ref _currentTheme, value);
        }

        /// <summary>Fired when the user clicks a theme button (name of new theme).</summary>
        public event Action<string> ThemeApplied;

        // ── Constructor ───────────────────────────────────────────────────
        public SettingsViewModel()
        {
            Load();
        }

        // ── Load / Save ───────────────────────────────────────────────────
        public void Load()
        {
            var cfg = ConfigService.Instance;

            AnthropicKey = cfg.GetApiKeyForProvider("Anthropic");
            OpenAIKey    = cfg.GetApiKeyForProvider("OpenAI");
            OpenAIBase   = cfg.GetBaseUrlForProvider("OpenAI");
            GeminiKey    = cfg.GetApiKeyForProvider("Gemini");
            OllamaBase   = cfg.GetBaseUrlForProvider("Ollama");
            OllamaModels = cfg.OllamaModels;
            AzureNodes   = cfg.AzureOpenAINodes;

            Temperature     = cfg.Temperature;
            MaxTokensText   = cfg.MaxTokens.ToString();
            StreamingEnabled = cfg.StreamingEnabled;
            Language        = cfg.Language;

            FontSize   = cfg.ChatFontSize;
            FontFamily = cfg.ChatFontFamily;

            SystemPrompt = SystemPromptService.Instance.GetSystemPrompt();
            CurrentTheme = ThemeService.CurrentName;
        }

        public void Save()
        {
            var cfg = ConfigService.Instance;

            cfg.SetApiKeyForProvider("Anthropic", AnthropicKey?.Trim() ?? "");
            cfg.SetApiKeyForProvider("OpenAI",    OpenAIKey?.Trim() ?? "");
            cfg.SetBaseUrlForProvider("OpenAI",   OpenAIBase?.Trim() ?? "");
            cfg.SetApiKeyForProvider("Gemini",    GeminiKey?.Trim() ?? "");
            cfg.SetBaseUrlForProvider("Ollama",   OllamaBase?.Trim() ?? "http://localhost:11434");
            cfg.OllamaModels     = OllamaModels?.Trim() ?? "";
            cfg.AzureOpenAINodes = AzureNodes?.Trim() ?? "";

            cfg.Temperature      = Temperature;
            cfg.StreamingEnabled = StreamingEnabled;
            cfg.Language         = Language ?? "zh-TW";

            if (int.TryParse(MaxTokensText, out int maxTok))
                cfg.MaxTokens = maxTok;

            cfg.ChatFontSize   = FontSize;
            cfg.ChatFontFamily = FontFamily ?? "Comic Sans MS";

            var sp = SystemPrompt?.Trim();
            if (!string.IsNullOrEmpty(sp))
                SystemPromptService.Instance.SetCustomPrompt(sp);
            else
                SystemPromptService.Instance.ClearCustomPrompt();

            UserSettingsService.Instance.SnapshotFromConfig();
            UserSettingsService.Instance.Save();
        }

        public void ApplyTheme(string themeName)
        {
            ThemeService.Apply(themeName);
            ConfigService.Instance.ThemeName = themeName;
            UserSettingsService.Instance.ThemeName = themeName;
            UserSettingsService.Instance.Save();
            CurrentTheme = themeName;
            ThemeApplied?.Invoke(themeName);
        }
    }
}
