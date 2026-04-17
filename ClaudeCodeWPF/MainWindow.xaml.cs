using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;
using OpenClaudeCodeWPF.Services.DocumentProcessing;
using OpenClaudeCodeWPF.Services.MCP;
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
                    ChatPanel.StartToolAnimation(id);
                    _vm.StatusMessage = $"⚙️ 執行: {name}";
                });
            _chatService.OnToolCompleted += (name, id, result) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ToolOutputPanel.AddToolResult(name, result);
                    ChatPanel.CompleteToolBubble(id, true);
                    _vm.StatusMessage = $"✅ 完成: {name}";
                });
            _chatService.OnToolFailed += (name, id, error) =>
                Dispatcher.InvokeAsync(() =>
                {
                    ToolOutputPanel.AddToolError(name, error);
                    ChatPanel.CompleteToolBubble(id, false);
                    _vm.StatusMessage = $"❌ 失敗: {name}";
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

            // Wire up HistoryPanel toggle → sidebar collapse
            HistoryPanel.OnToggleRequested += () => ToggleSidebar();
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
                        ChatPanel.ShowToolCall(evt.ToolCall.Name, evt.ToolCall.Id, evt.ToolCall.Arguments?.ToString());
                    break;

                case StreamEventType.ToolResultsReady:
                    ChatPanel.ShowToolResultsDivider();
                    break;

                case StreamEventType.MessageEnd:
                    ChatPanel.FinalizeAssistantMessage();
                    // Update token display whenever we get usage info
                    if (evt.Usage != null)
                        _vm.TokenInfo = $"↑{evt.Usage.InputTokens}  ↓{evt.Usage.OutputTokens}";
                    // Only re-enable input when the ENTIRE turn loop is done
                    if (evt.IsFinalTurn)
                    {
                        _vm.StatusMessage = $"就緒  ·  {ConfigService.Instance.CurrentProvider} / {ConfigService.Instance.CurrentModel}";
                        ChatPanel.SetSendEnabled(true);
                        // Fire-and-forget AI title generation for new conversations
#pragma warning disable CS4014
                        TryGenerateTitleAsync();
#pragma warning restore CS4014
                    }
                    else
                    {
                        // Intermediate round (tool calls follow) — show progress in status bar
                        _vm.StatusMessage = "執行工具中...";
                    }
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

        private async void OnSendMessage(string message, IReadOnlyList<AttachedFileInfo> files)
        {
            // Build final message: original text + extracted file contents
            var finalMessage = await BuildMessageWithFiles(message, files);
            if (string.IsNullOrWhiteSpace(finalMessage)) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            ChatPanel.SetSendEnabled(false);
            _vm.StatusMessage = "傳送中...";

            var session = ConversationManager.Instance.GetOrCreateActiveSession();

            // Show user message in UI: original text + file names (not full content)
            var displayMsg = BuildDisplayMessage(message, files);
            ChatPanel.AddUserMessage(displayMsg);
            // Show animated bubble right after user message while waiting for HTTP
            ChatPanel.ShowWaitingIndicator();

            try
            {
                // Run on background thread so streaming renders each chunk
                // instead of batching all updates at the end on the UI thread
                await Task.Run(() => _chatService.SendMessageAsync(session, finalMessage, _cts.Token));
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

        private static string BuildDisplayMessage(string message, IReadOnlyList<AttachedFileInfo> files)
        {
            if (files == null || files.Count == 0)
                return message;
            var names = string.Join(", ", files.Select(f => f.FileName));
            return string.IsNullOrWhiteSpace(message)
                ? $"📎 {names}"
                : $"{message}\n📎 {names}";
        }

        private async Task<string> BuildMessageWithFiles(string message, IReadOnlyList<AttachedFileInfo> files)
        {
            if (files == null || files.Count == 0)
                return string.IsNullOrWhiteSpace(message) ? null : message;

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(message))
                sb.AppendLine(message).AppendLine();

            foreach (var file in files)
            {
                sb.AppendLine($"--- 附件: {file.FileName} ({file.DisplaySize}) ---");
                try
                {
                    var content = await Task.Run(() =>
                        DocumentExtractor.ExtractText(file.FilePath));
                    sb.AppendLine(string.IsNullOrWhiteSpace(content)
                        ? "[無法擷取文字內容]"
                        : content);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[讀取失敗: {ex.Message}]");
                }
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
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

            // Restore sidebar collapsed/expanded state (default: collapsed)
            var svc = UserSettingsService.Instance;
            bool collapsed = svc.SidebarCollapsed;
            HistoryPanel.SetCollapsed(collapsed);
            if (collapsed)
            {
                LeftPanelColumn.Width    = new System.Windows.GridLength(0);
                LeftSplitterColumn.Width = new System.Windows.GridLength(0);
            }

            // Load or create initial session
            var session = ConversationManager.Instance.GetOrCreateActiveSession();
            ChatPanel.LoadSession(session);

            // Auto-connect enabled MCP servers (fire and forget)
            AutoConnectMcpServersAsync();
        }

        private async void AutoConnectMcpServersAsync()
        {
            var configs = MCPConfigService.Instance.LoadAll();
            foreach (var cfg in configs)
            {
                if (!cfg.Enabled) continue;
                try
                {
                    await MCPConnectionManager.Instance.ConnectAsync(cfg, CancellationToken.None);
                }
                catch
                {
                    // Non-fatal: log or ignore per-server errors
                }
            }
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
            _vm.StatusMessage = $"正在載入 {provider} 模型清單…";

            try
            {
                var p = ModelProviderFactory.Instance.GetProvider(provider);
                var models = await p.GetAvailableModelsAsync();
                _vm.SetModels(models, ConfigService.Instance.CurrentModel);
                _vm.StatusMessage = $"就緒  ·  {provider}  ({models.Count} 個模型)";
            }
            catch (Exception ex)
            {
                _vm.StatusMessage = $"無法載入 {provider} 模型: {ex.Message}";
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

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logPath = LogService.Instance.TodayLogPath;
                // Open the log file if it exists, otherwise open the log folder
                if (System.IO.File.Exists(logPath))
                    System.Diagnostics.Process.Start(logPath);
                else
                    System.Diagnostics.Process.Start(LogService.Instance.LogDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"無法開啟日誌：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Kept for HandleStreamEvent's MessageStart which still calls it inline.</summary>
        private void SetStatus(string text) => _vm.StatusMessage = text;

        private void SidebarToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleSidebar();
        }

        // ── Sidebar collapse / expand ─────────────────────────────────────────

        private void ToggleSidebar()
        {
            var svc       = UserSettingsService.Instance;
            bool collapse = LeftPanelColumn.Width.Value > 0;

            if (collapse)
            {
                // Save width before collapsing so we can restore it later
                svc.SidebarWidth         = LeftPanelColumn.Width.Value;
                LeftPanelColumn.Width    = new System.Windows.GridLength(0);
                LeftSplitterColumn.Width = new System.Windows.GridLength(0);
                HistoryPanel.SetCollapsed(true);
                svc.SidebarCollapsed = true;
            }
            else
            {
                double restoreWidth = svc.SidebarWidth > 0 ? svc.SidebarWidth : 240;
                LeftPanelColumn.Width    = new System.Windows.GridLength(restoreWidth);
                LeftSplitterColumn.Width = new System.Windows.GridLength(4);
                HistoryPanel.SetCollapsed(false);
                svc.SidebarCollapsed = false;
            }

            svc.Save();
        }

        // ── AI conversation title generation ─────────────────────────────────

        /// <summary>
        /// After the first AI response in a new session, ask AI to generate
        /// a short descriptive title. Fire-and-forget; fallback to text truncation.
        /// </summary>
        private async Task TryGenerateTitleAsync()
        {
            var session = ConversationManager.Instance.GetOrCreateActiveSession();
            if (session.Title != "新對話") return;

            var firstUser = session.Messages.FirstOrDefault(m =>
                m.Role == "user" && !string.IsNullOrWhiteSpace(m.Content));
            var firstAssistant = session.Messages.FirstOrDefault(m =>
                m.Role == "assistant" && !string.IsNullOrWhiteSpace(m.Content));
            if (firstUser == null) return;

            var title = await TitleGeneratorService.Instance.GenerateTitleAsync(
                firstUser.Content, firstAssistant?.Content ?? "");

            if (!string.IsNullOrWhiteSpace(title))
                session.Title = title;
            else
                session.UpdateTitle(); // fallback: truncate first message

            session.UpdatedAt = DateTime.UtcNow;
            ConversationManager.Instance.SaveSession(session);
            _ = Dispatcher.InvokeAsync(() => HistoryPanel.Refresh());
        }
    }
}
