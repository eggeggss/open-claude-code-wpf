using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;

namespace OpenClaudeCodeWPF
{
    public partial class MainWindow : Window
    {
        private readonly ChatService _chatService;
        private CancellationTokenSource _cts;
        private bool _isStreamingEnabled;

        public bool IsStreamingEnabled
        {
            get => _isStreamingEnabled;
            set
            {
                _isStreamingEnabled = value;
                ConfigService.Instance.StreamingEnabled = value;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _chatService = new ChatService();
            _isStreamingEnabled = ConfigService.Instance.StreamingEnabled;

            WireUpChatService();
        }

        private void WireUpChatService()
        {
            _chatService.OnEvent += evt => Dispatcher.InvokeAsync(() => HandleStreamEvent(evt));
            _chatService.OnToolStarted += (name, id, input) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ToolOutputPanel.AddToolStart(name, input);
                    SetStatus($"執行工具: {name}");
                });
            _chatService.OnToolCompleted += (name, id, result) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ToolOutputPanel.AddToolResult(name, result);
                    SetStatus($"工具完成: {name}");
                });
            _chatService.OnToolFailed += (name, id, error) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ToolOutputPanel.AddToolError(name, error);
                    SetStatus($"工具失敗: {name}");
                });

            // Wire up ChatPanel's send action
            ChatPanel.OnSendMessage += OnSendMessage;
            ChatPanel.OnCancelRequested += OnCancelRequested;
            ChatPanel.OnSlashCommand += HandleSlashCommand;

            // Wire up slash command service callbacks
            var slashSvc = SlashCommandService.Instance;
            slashSvc.ShowMessage     = (msg, isErr, fs, ff) => Dispatcher.Invoke(() => ChatPanel.ShowSystemMessage(msg, isErr, fs, ff));
            slashSvc.RefreshChat     = () => Dispatcher.Invoke(() =>
            {
                var sess = ConversationManager.Instance.GetOrCreateActiveSession();
                ChatPanel.LoadSession(sess);
            });
            slashSvc.RefreshHistory  = () => Dispatcher.Invoke(() => HistoryPanel.Refresh());

            HistoryPanel.Initialize(ConversationManager.Instance);
            HistoryPanel.OnSessionSelected += HistoryPanel_SessionSelected;

            // Wire up ToolsPanel click → focus input with hint
            ToolsPanel.OnToolClicked += (toolName, desc) =>
                Dispatcher.InvokeAsync(() => ChatPanel.InsertToolHint(toolName));
        }

        private void HandleStreamEvent(StreamEvent evt)
        {
            switch (evt.Type)
            {
                case StreamEventType.MessageStart:
                    ChatPanel.StartAssistantMessage();
                    SetStatus("AI 正在思考...");
                    break;

                case StreamEventType.TextDelta:
                    ChatPanel.AppendAssistantText(evt.TextDelta ?? "");
                    break;

                case StreamEventType.ThinkingDelta:
                    ChatPanel.AppendThinkingText(evt.TextDelta ?? "");
                    break;

                case StreamEventType.ToolCallStart:
                    if (evt.ToolCall != null)
                        ChatPanel.ShowToolCall(evt.ToolCall.Name, evt.ToolCall.Arguments?.ToString());
                    break;

                case StreamEventType.ToolResultsReady:
                    ChatPanel.ShowToolResultsDivider();
                    break;

                case StreamEventType.MessageEnd:
                    ChatPanel.FinalizeAssistantMessage();
                    SetStatus($"就緒  ·  {ConfigService.Instance.CurrentProvider} / {ConfigService.Instance.CurrentModel}");
                    if (evt.Usage != null)
                        TokenText.Text = $"↑{evt.Usage.InputTokens}  ↓{evt.Usage.OutputTokens}";
                    ChatPanel.SetSendEnabled(true);
                    break;

                case StreamEventType.Error:
                    ChatPanel.ShowError(evt.Error);
                    SetStatus($"錯誤: {evt.Error}");
                    ChatPanel.SetSendEnabled(true);
                    break;

                case StreamEventType.ContextWarning:
                    UpdateContextIndicator(evt.ContextPercent, evt.TrimmedCount);
                    break;
            }
        }

        /// <summary>更新狀態列的上下文使用率指示器。</summary>
        private void UpdateContextIndicator(double percent, int trimmedCount)
        {
            string label = $"Ctx {percent:F0}%";
            if (trimmedCount > 0)
                label += $" ✂{trimmedCount}";

            ContextText.Text = label;
            ContextText.Visibility = System.Windows.Visibility.Visible;

            // 顏色：綠→黃→橙→紅
            if (percent >= ContextManager.ErrorThreshold)
                ContextText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x55, 0x55)); // 紅
            else if (percent >= ContextManager.WarnThreshold)
                ContextText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xCC, 0x44)); // 橙黃
            else
                ContextText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x88, 0xDD, 0x88)); // 淺綠
        }

        private async void OnSendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            ChatPanel.SetSendEnabled(false);
            SetStatus("傳送中...");

            var session = ConversationManager.Instance.GetOrCreateActiveSession();

            // Show user message immediately in UI (before typing bubble)
            ChatPanel.AddUserMessage(message);
            // Show animated bubble right after user message while waiting for HTTP
            ChatPanel.ShowWaitingIndicator();

            try
            {
                // Run on background thread so streaming renders each chunk
                // instead of batching all updates at the end on the UI thread
                await Task.Run(() => _chatService.SendMessageAsync(session, message, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                ChatPanel.ShowError("已取消");
                SetStatus("已取消");
                ChatPanel.SetSendEnabled(true);
            }
            catch (Exception ex)
            {
                ChatPanel.ShowError(ex.Message);
                SetStatus($"錯誤: {ex.Message}");
                ChatPanel.SetSendEnabled(true);
            }
        }

        private void OnCancelRequested()
        {
            _cts?.Cancel();
            SetStatus("取消中...");
        }

        private void HandleSlashCommand(string input)
        {
            SlashCommandService.Instance.TryHandle(input);
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Escape → cancel current generation
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (_cts != null && !_cts.IsCancellationRequested)
                {
                    OnCancelRequested();
                    e.Handled = true;
                }
            }
            // Ctrl+N → new conversation
            else if (e.Key == System.Windows.Input.Key.N &&
                     System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                NewChatButton_Click(null, null);
                e.Handled = true;
            }
        }

        private void HistoryPanel_SessionSelected(ConversationSession session)
        {
            ConversationManager.Instance.SetActiveSession(session);
            ChatPanel.LoadSession(session);
            ToolOutputPanel.Clear();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore persisted settings into ConfigService
            UserSettingsService.Instance.ApplyToConfig();

            // Apply saved theme (must run before UI is shown)
            ThemeService.Apply(ConfigService.Instance.ThemeName);

            // Reload chat when theme changes so new bubble colors appear
            ThemeService.ThemeChanged += (s, _) =>
                Dispatcher.InvokeAsync(() =>
                {
                    var sess = ConversationManager.Instance.GetOrCreateActiveSession();
                    ChatPanel.LoadSession(sess);
                });

            // Populate providers
            var providers = new List<string> { "Anthropic", "OpenAI", "AzureOpenAI", "Gemini", "Ollama" };
            ProviderComboBox.ItemsSource = providers;
            ProviderComboBox.SelectedItem = ConfigService.Instance.CurrentProvider;

            StreamToggle.IsChecked = IsStreamingEnabled;

            // Apply saved font to chat panel
            ChatPanel.UpdateFontSettings(
                ConfigService.Instance.ChatFontSize,
                ConfigService.Instance.ChatFontFamily);

            // Load or create initial session
            var session = ConversationManager.Instance.GetOrCreateActiveSession();
            ChatPanel.LoadSession(session);

            SetStatus($"就緒  ·  {ConfigService.Instance.CurrentProvider} / {ConfigService.Instance.CurrentModel}");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();
            ConversationManager.Instance.SaveActiveSession();
            // Save current provider/model and all settings on exit
            UserSettingsService.Instance.SnapshotFromConfig();
            UserSettingsService.Instance.Save();
        }

        private async void ProviderComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem == null) return;
            var provider = ProviderComboBox.SelectedItem.ToString();
            ConfigService.Instance.CurrentProvider = provider;

            // Load available models
            try
            {
                var p = ModelProviderFactory.Instance.GetProvider(provider);
                var models = await p.GetAvailableModelsAsync();
                ModelComboBox.ItemsSource = models;
                if (models.Count > 0)
                {
                    var current = ConfigService.Instance.CurrentModel;
                    var selected = models.Find(m => m.Id == current) ?? models[0];
                    ModelComboBox.SelectedItem = selected;
                }
            }
            catch (Exception ex)
            {
                SetStatus($"無法載入模型: {ex.Message}");
            }
        }

        private void ModelComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ModelComboBox.SelectedItem is ModelInfo m)
            {
                ConfigService.Instance.CurrentModel = m.Id;
                SetStatus($"就緒  ·  {ConfigService.Instance.CurrentProvider} / {m.Id}");
                // Persist immediately so restart remembers the model
                UserSettingsService.Instance.CurrentProvider = ConfigService.Instance.CurrentProvider;
                UserSettingsService.Instance.CurrentModel    = m.Id;
                UserSettingsService.Instance.Save();
            }
        }

        private void StreamToggle_Changed(object sender, RoutedEventArgs e)
        {
            IsStreamingEnabled = StreamToggle.IsChecked ?? true;
        }

        private void NewChatButton_Click(object sender, RoutedEventArgs e)
        {
            ConversationManager.Instance.SaveActiveSession();
            var session = ConversationManager.Instance.CreateSession();
            ChatPanel.LoadSession(session);
            ToolOutputPanel.Clear();
            HistoryPanel.Refresh();
            // Reset context indicator for new session
            ContextText.Visibility = System.Windows.Visibility.Collapsed;
            ContextText.Text = "";
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Views.SettingsPanel();
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                // Apply font settings immediately to all existing messages
                ChatPanel.UpdateFontSettings(
                    ConfigService.Instance.ChatFontSize,
                    ConfigService.Instance.ChatFontFamily);
            }
        }

        private void SetStatus(string text)
        {
            StatusText.Text = text;
        }
    }
}
