using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;
using OpenClaudeCodeWPF.ViewModels;

namespace OpenClaudeCodeWPF
{
    public partial class MainWindow : Window
    {
        private readonly ChatService _chatService;
        private CancellationTokenSource _cts;
        private MainWindowViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainWindowViewModel();
            DataContext = _vm;

            _chatService = new ChatService();

            WireUpChatService();
        }

        private void WireUpChatService()
        {
            _chatService.OnEvent += evt => Dispatcher.InvokeAsync(() => HandleStreamEvent(evt));
            _chatService.OnToolStarted += (name, id, input) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ToolOutputPanel.AddToolStart(name, input);
                    _vm.StatusMessage = $"執行工具: {name}";
                });
            _chatService.OnToolCompleted += (name, id, result) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ToolOutputPanel.AddToolResult(name, result);
                    _vm.StatusMessage = $"工具完成: {name}";
                });
            _chatService.OnToolFailed += (name, id, error) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ToolOutputPanel.AddToolError(name, error);
                    _vm.StatusMessage = $"工具失敗: {name}";
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
                    _vm.StatusMessage = $"就緒  ·  {ConfigService.Instance.CurrentProvider} / {ConfigService.Instance.CurrentModel}";
                    if (evt.Usage != null)
                        _vm.TokenInfo = $"↑{evt.Usage.InputTokens}  ↓{evt.Usage.OutputTokens}";
                    ChatPanel.SetSendEnabled(true);
                    break;

                case StreamEventType.Error:
                    ChatPanel.ShowError(evt.Error);
                    _vm.StatusMessage = $"錯誤: {evt.Error}";
                    ChatPanel.SetSendEnabled(true);
                    break;

                case StreamEventType.ContextWarning:
                    _vm.UpdateContextIndicator(evt.ContextPercent, evt.TrimmedCount);
                    break;
            }
        }

        private async void OnSendMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            ChatPanel.SetSendEnabled(false);
            _vm.StatusMessage = "傳送中...";

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
                _vm.StatusMessage = "已取消";
                ChatPanel.SetSendEnabled(true);
            }
            catch (Exception ex)
            {
                ChatPanel.ShowError(ex.Message);
                _vm.StatusMessage = $"錯誤: {ex.Message}";
                ChatPanel.SetSendEnabled(true);
            }
        }

        private void OnCancelRequested()
        {
            _cts?.Cancel();
            _vm.StatusMessage = "取消中...";
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

            // Provider ComboBox is populated via VM Providers binding.
            // Triggering SelectedItem via VM initialises model list through SelectionChanged.
            ProviderComboBox.SelectedItem = ConfigService.Instance.CurrentProvider;

            // Apply saved font to chat panel
            ChatPanel.UpdateFontSettings(
                ConfigService.Instance.ChatFontSize,
                ConfigService.Instance.ChatFontFamily);

            // Load or create initial session
            var session = ConversationManager.Instance.GetOrCreateActiveSession();
            ChatPanel.LoadSession(session);
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
            var provider = ProviderComboBox.SelectedItem?.ToString();
            if (provider == null) return;

            // Keep VM in sync (VM property setter updates ConfigService)
            _vm.CurrentProvider = provider;

            try
            {
                var p = ModelProviderFactory.Instance.GetProvider(provider);
                var models = await p.GetAvailableModelsAsync();
                _vm.SetModels(models, ConfigService.Instance.CurrentModel);
            }
            catch (Exception ex)
            {
                _vm.StatusMessage = $"無法載入模型: {ex.Message}";
            }
        }

        private void StreamToggle_Changed(object sender, RoutedEventArgs e)
        {
            _vm.IsStreamingEnabled = StreamToggle.IsChecked ?? true;
        }

        private void NewChatButton_Click(object sender, RoutedEventArgs e)
        {
            ConversationManager.Instance.SaveActiveSession();
            var session = ConversationManager.Instance.CreateSession();
            ChatPanel.LoadSession(session);
            ToolOutputPanel.Clear();
            HistoryPanel.Refresh();
            _vm.ClearContext();
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

        /// <summary>Kept for HandleStreamEvent's MessageStart which still calls it inline.</summary>
        private void SetStatus(string text) => _vm.StatusMessage = text;
    }
}
