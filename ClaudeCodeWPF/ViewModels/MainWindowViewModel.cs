using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;

namespace OpenClaudeCodeWPF.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        // ── Status bar ──────────────────────────────────────────────────

        private string _statusMessage = "就緒";
        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        private string _tokenInfo = "";
        public string TokenInfo
        {
            get => _tokenInfo;
            set => Set(ref _tokenInfo, value);
        }

        // ── Context indicator ───────────────────────────────────────────

        private string _contextLabel = "";
        public string ContextLabel
        {
            get => _contextLabel;
            set => Set(ref _contextLabel, value);
        }

        private bool _isContextVisible;
        public bool IsContextVisible
        {
            get => _isContextVisible;
            set => Set(ref _isContextVisible, value);
        }

        private SolidColorBrush _contextColor
            = new SolidColorBrush(Color.FromRgb(0x88, 0xDD, 0x88));
        public SolidColorBrush ContextColor
        {
            get => _contextColor;
            set => Set(ref _contextColor, value);
        }

        // ── Provider / Model ────────────────────────────────────────────

        public List<string> Providers { get; } =
            new List<string> { "Anthropic", "OpenAI", "AzureOpenAI", "Gemini", "Ollama", "OpenRouter" };

        private string _currentProvider;
        public string CurrentProvider
        {
            get => _currentProvider;
            set
            {
                if (_currentProvider == value) return;
                _currentProvider = value;
                OnPropertyChanged();
                ConfigService.Instance.CurrentProvider = value;
            }
        }

        private ObservableCollection<ModelInfo> _availableModels
            = new ObservableCollection<ModelInfo>();
        public ObservableCollection<ModelInfo> AvailableModels
        {
            get => _availableModels;
            private set => Set(ref _availableModels, value);
        }

        private ModelInfo _selectedModel;
        public ModelInfo SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (!Set(ref _selectedModel, value) || value == null) return;
                ConfigService.Instance.CurrentModel = value.Id;
                ConfigService.Instance.CurrentModelSupportsVision = value.SupportsVision;
                StatusMessage = $"就緒  ·  {CurrentProvider} / {value.Id}";
                UserSettingsService.Instance.CurrentProvider = CurrentProvider;
                UserSettingsService.Instance.CurrentModel    = value.Id;
                UserSettingsService.Instance.Save();
            }
        }

        // ── Streaming ───────────────────────────────────────────────────

        private bool _isStreamingEnabled;
        public bool IsStreamingEnabled
        {
            get => _isStreamingEnabled;
            set
            {
                Set(ref _isStreamingEnabled, value);
                ConfigService.Instance.StreamingEnabled = value;
            }
        }

        public string AppVersion
        {
            get
            {
                var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return $"v{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        // ── Constructor ─────────────────────────────────────────────────

        public MainWindowViewModel()
        {
            _currentProvider    = ConfigService.Instance.CurrentProvider;
            _isStreamingEnabled = ConfigService.Instance.StreamingEnabled;
        }

        // ── Helper methods (called from code-behind) ────────────────────

        /// <summary>Update context indicator after model response.</summary>
        public void UpdateContextIndicator(double percent, int trimmedCount)
        {
            string label = $"Ctx {percent:F0}%";
            if (trimmedCount > 0) label += $" ✂{trimmedCount}";
            ContextLabel = label;
            IsContextVisible = true;

            if (percent >= ContextManager.ErrorThreshold)
                ContextColor = new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
            else if (percent >= ContextManager.WarnThreshold)
                ContextColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x44));
            else
                ContextColor = new SolidColorBrush(Color.FromRgb(0x88, 0xDD, 0x88));
        }

        /// <summary>
        /// Replace the model list; select the preferred model or fall back to index 0.
        /// If the preferred model is not in the fetched list (e.g. custom/private model),
        /// insert it at the top so the user's selection is always preserved.
        /// Sets the backing field directly to avoid triggering the persistence side-effect.
        /// </summary>
        public void SetModels(List<ModelInfo> models, string preferredId = null)
        {
            // If the saved model isn't in the fetched list, insert it as a custom entry
            // so the user's previous selection is always honoured on restart.
            if (!string.IsNullOrEmpty(preferredId) &&
                !models.Exists(m => string.Equals(m.Id, preferredId, StringComparison.OrdinalIgnoreCase)))
            {
                models.Insert(0, new ModelInfo
                {
                    Id              = preferredId,
                    DisplayName     = preferredId,
                    Provider        = CurrentProvider,
                    MaxTokens       = 8192,
                    SupportsTools   = true,
                    SupportsVision  = false,
                    SupportsThinking = false
                });
            }

            AvailableModels = new ObservableCollection<ModelInfo>(models);
            if (models.Count == 0) return;

            var match = models.Find(m => string.Equals(m.Id, preferredId, StringComparison.OrdinalIgnoreCase))
                        ?? models[0];
            // Set backing field so we don't double-save on initial population
            _selectedModel = match;
            OnPropertyChanged(nameof(SelectedModel));

            ConfigService.Instance.CurrentModel = match.Id;
            ConfigService.Instance.CurrentModelSupportsVision = match.SupportsVision;
            StatusMessage = $"就緒  ·  {CurrentProvider} / {match.Id}";
        }

        /// <summary>Clear the context indicator (e.g. new conversation).</summary>
        public void ClearContext()
        {
            IsContextVisible = false;
            ContextLabel     = "";
        }
    }
}
