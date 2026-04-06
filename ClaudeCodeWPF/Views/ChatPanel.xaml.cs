using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;
using OpenClaudeCodeWPF.Utils;
using OpenClaudeCodeWPF.ViewModels;

namespace OpenClaudeCodeWPF.Views
{
    public partial class ChatPanel : UserControl
    {
        // ── Events exposed to MainWindow ──────────────────────────────────
        public event Action<string, IReadOnlyList<AttachedFileInfo>> OnSendMessage;
        public event Action OnCancelRequested;
        public event Action<string> OnSlashCommand;

        // ── ViewModel ─────────────────────────────────────────────────────
        private readonly ChatViewModel _vm;

        // ── Streaming / UI state ──────────────────────────────────────────
        private TextBox _currentAssistantBlock;
        private TextBox _currentThinkingBlock;
        private Border _thinkingContainer;
        private UIElement _thinkingContent;
        private TextBlock _thinkingHeaderLabel;
        private TextBlock _thinkingArrow;
        private bool _assistantContainerCreated;

        // Typing bubble
        private Border _typingBubble;
        private Ellipse[] _typingDots;
        private DispatcherTimer _typingTimer;
        private int _typingFrame;

        // ── Tool execution bubble tracking ────────────────────────────────
        private class ToolBubbleState
        {
            public TextBlock IconLabel;
            public TextBlock StatusLabel;
            public DispatcherTimer Timer;
            public DateTime StartTime;
        }
        private readonly Dictionary<string, ToolBubbleState> _toolBubbles
            = new Dictionary<string, ToolBubbleState>();
        private static readonly string[] DotFrames = { "·  ", "·· ", "···" };

        // Font settings
        private double _msgFontSize = 13;
        private FontFamily _msgFontFamily = new FontFamily("Segoe UI");

        public ChatPanel()
        {
            InitializeComponent();

            _vm = new ChatViewModel();
            DataContext = _vm;

            // Bridge VM events → ChatPanel public events (for MainWindow)
            _vm.SendRequested         += (text, files) => OnSendMessage?.Invoke(text, files);
            _vm.SlashCommandRequested += text => { AddUserMessage(text); OnSlashCommand?.Invoke(text); };
            _vm.CancelRequested       += ()   => OnCancelRequested?.Invoke();

            _msgFontSize   = ConfigService.Instance.ChatFontSize;
            _msgFontFamily = new FontFamily(ConfigService.Instance.ChatFontFamily);
            Loaded += (s, e) => ApplyInputBoxFont();
        }

        private void ApplyInputBoxFont()
        {
            InputBox.FontSize   = _msgFontSize;
            InputBox.FontFamily = _msgFontFamily;
        }

        public void LoadSession(ConversationSession session)
        {
            MessagesPanel.Children.Clear();
            _currentAssistantBlock = null;

            if (session == null) { UpdateEmptyState(); return; }

            foreach (var msg in session.Messages)
            {
                if (msg.Role == "user")
                    AddUserMessage(msg.Content);
                else if (msg.Role == "assistant")
                    AddAssistantMessage(msg.Content, msg.ToolCalls);
            }

            UpdateEmptyState();
            ScrollToBottom();
        }

        private void UpdateEmptyState()
        {
            bool isEmpty = MessagesPanel.Children.Count == 0;
            EmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>點擊工具清單中的工具卡片時，在輸入框填入使用提示</summary>
        public void InsertToolHint(string toolName)
        {
            InputBox.Text = $"請使用 {toolName} 工具 ";
            InputBox.CaretIndex = InputBox.Text.Length;
            InputBox.Focus();
        }

        /// <summary>套用新字體設定：更新變數並即時更新所有既有訊息元素</summary>
        public void UpdateFontSettings(double fontSize, string fontFamily)
        {
            _msgFontSize = fontSize;
            _msgFontFamily = new FontFamily(fontFamily);
            ApplyInputBoxFont();
            ApplyFontToPanel(MessagesPanel);
        }

        private void ApplyFontToPanel(DependencyObject parent)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox tb && "msg".Equals(tb.Tag?.ToString()))
                {
                    tb.FontSize = _msgFontSize;
                    tb.FontFamily = _msgFontFamily;
                }
                ApplyFontToPanel(child);
            }
        }

        public void AddUserMessage(string content)
        {
            var container = new Border
            {
                Background = new SolidColorBrush(ThemeService.Current.UserBubble),
                Padding = new Thickness(12, 8, 12, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 600
            };

            var tb = new TextBox
            {
                Text = content,
                Foreground = new SolidColorBrush(ThemeService.Current.TextPrimary),
                FontSize = _msgFontSize,
                FontFamily = _msgFontFamily,
                Tag = "msg",
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.IBeam,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            container.Child = tb;
            MessagesPanel.Children.Add(container);
            UpdateEmptyState();
            ScrollToBottom();
        }

        /// <summary>在用戶訊息之後立即顯示等待動畫（發送後到 MessageStart 之間的空窗期）</summary>
        public void ShowWaitingIndicator()
        {
            ShowTypingBubble();
            ScrollToBottom();
        }

        public void StartAssistantMessage()
        {
            _currentThinkingBlock = null;
            _currentAssistantBlock = null;
            _thinkingContainer = null;
            _thinkingContent = null;
            _thinkingHeaderLabel = null;
            _thinkingArrow = null;
            _assistantContainerCreated = false;
            // Stop any leftover tool timers from the previous turn
            foreach (var s in _toolBubbles.Values) s.Timer?.Stop();
            _toolBubbles.Clear();
            ShowTypingBubble();  // show bubble AFTER user message is in the panel
            ScrollToBottom();
        }

        // Creates the "◆ Claude" container the first time content (text/tool/thinking) arrives.
        private void EnsureAssistantContainer()
        {
            if (_assistantContainerCreated) return;
            _assistantContainerCreated = true;

            RemoveTypingBubble(); // swap typing bubble for the real container

            var container = new Border
            {
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 4, 40, 4),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var stack = new StackPanel();

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(new TextBlock
            {
                Text = "◆ Open Claude",
                Foreground = new SolidColorBrush(ThemeService.Current.Accent),
                FontSize = 12, FontWeight = FontWeights.Bold
            });
            stack.Children.Add(header);

            _currentAssistantBlock = new TextBox
            {
                Foreground = new SolidColorBrush(ThemeService.Current.TextPrimary),
                FontSize = _msgFontSize,
                FontFamily = _msgFontFamily,
                Tag = "msg",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 700,
                IsReadOnly = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Cursor = Cursors.IBeam,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            stack.Children.Add(_currentAssistantBlock);
            container.Child = stack;
            MessagesPanel.Children.Add(container);
        }

        public void AppendAssistantText(string text)
        {
            EnsureAssistantContainer();
            _currentAssistantBlock.Text += text;
            ThinkingIndicator.Visibility = Visibility.Collapsed;
            ScrollToBottom();
        }

        public void AppendThinkingText(string text)
        {
            EnsureAssistantContainer();
            if (_currentThinkingBlock == null)
            {
                // ── Outer container ─────────────────────────────────────────
                _thinkingContainer = new Border
                {
                    Background      = new SolidColorBrush(System.Windows.Media.Color.FromArgb(22, 128, 128, 128)),
                    BorderBrush     = new SolidColorBrush(System.Windows.Media.Color.FromArgb(55, 128, 128, 128)),
                    BorderThickness = new Thickness(1),
                    CornerRadius    = new CornerRadius(6),
                    Margin          = new Thickness(0, 0, 0, 6)
                };

                var outerStack = new StackPanel();

                // ── Header row (click to expand/collapse) ───────────────────
                var headerBorder = new Border
                {
                    Padding = new Thickness(8, 5, 8, 5),
                    Cursor  = Cursors.Hand
                };
                var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
                _thinkingHeaderLabel = new TextBlock
                {
                    Text       = "💭 思考中...",
                    Foreground = new SolidColorBrush(ThemeService.Current.TextSecondary),
                    FontSize   = 11,
                    FontStyle  = FontStyles.Italic,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _thinkingArrow = new TextBlock
                {
                    Text       = " ▲",
                    Foreground = new SolidColorBrush(ThemeService.Current.TextSecondary),
                    FontSize   = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerRow.Children.Add(_thinkingHeaderLabel);
                headerRow.Children.Add(_thinkingArrow);
                headerBorder.Child = headerRow;

                // Capture locals for toggle closure
                Border capturedContainer = _thinkingContainer;
                TextBlock capturedArrow  = _thinkingArrow;
                bool[] expanded = { true };
                UIElement[] contentRef = { null };

                headerBorder.MouseLeftButtonDown += (s, e) =>
                {
                    if (contentRef[0] == null) return;
                    expanded[0] = !expanded[0];
                    contentRef[0].Visibility = expanded[0] ? Visibility.Visible : Visibility.Collapsed;
                    capturedArrow.Text = expanded[0] ? " ▲" : " ▼";
                };

                // ── Thinking text area ───────────────────────────────────────
                _currentThinkingBlock = new TextBox
                {
                    Foreground   = new SolidColorBrush(ThemeService.Current.TextSecondary),
                    FontSize     = Math.Max(10, _msgFontSize - 2),
                    FontFamily   = _msgFontFamily,
                    FontStyle    = FontStyles.Italic,
                    TextWrapping = TextWrapping.Wrap,
                    IsReadOnly   = true,
                    Background   = Brushes.Transparent,
                    BorderThickness  = new Thickness(0),
                    Padding      = new Thickness(0),
                    Cursor       = Cursors.IBeam,
                    MaxHeight    = 260,
                    VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };

                var contentBorder = new Border { Padding = new Thickness(8, 0, 8, 8) };
                contentBorder.Child = _currentThinkingBlock;
                _thinkingContent = contentBorder;
                contentRef[0]    = contentBorder;

                outerStack.Children.Add(headerBorder);
                outerStack.Children.Add(contentBorder);
                _thinkingContainer.Child = outerStack;

                // Insert before the assistant text block
                var parent = _currentAssistantBlock?.Parent as StackPanel;
                if (parent != null)
                {
                    var idx = parent.Children.IndexOf(_currentAssistantBlock);
                    parent.Children.Insert(Math.Max(0, idx), _thinkingContainer);
                }
            }
            _currentThinkingBlock.Text += text;
            ScrollToBottom();
        }

        /// <summary>
        /// Shows a tool call bubble. toolId is used to track the bubble for live updates.
        /// Call StartToolAnimation(toolId) when execution begins,
        /// and CompleteToolBubble(toolId, success) when it finishes.
        /// </summary>
        public void ShowToolCall(string toolName, string toolId, string input)
        {
            EnsureAssistantContainer();

            // ── Outer container ────────────────────────────────────────────
            var border = new Border
            {
                Background      = new SolidColorBrush(ThemeService.Current.ToolCallBg),
                BorderBrush     = new SolidColorBrush(ThemeService.Current.Border),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(4),
                Margin          = new Thickness(0, 4, 0, 4),
                Padding         = new Thickness(8, 6, 8, 6)
            };

            var outerStack = new StackPanel();

            // ── Header row: icon + tool name + status indicator ────────────
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconLabel = new TextBlock
            {
                Text       = "🔧",
                FontSize   = 12,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameLabel = new TextBlock
            {
                Text         = $" {toolName}",
                Foreground   = new SolidColorBrush(ThemeService.Current.Accent),
                FontSize     = 12,
                FontFamily   = new FontFamily("Consolas"),
                FontWeight   = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var statusLabel = new TextBlock
            {
                Text              = "準備中",
                FontSize          = 11,
                FontFamily        = new FontFamily("Consolas"),
                Foreground        = new SolidColorBrush(ThemeService.Current.TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Grid.SetColumn(iconLabel,   0);
            Grid.SetColumn(nameLabel,   1);
            Grid.SetColumn(statusLabel, 2);
            headerGrid.Children.Add(iconLabel);
            headerGrid.Children.Add(nameLabel);
            headerGrid.Children.Add(statusLabel);
            outerStack.Children.Add(headerGrid);

            // ── Input preview (truncated) ─────────────────────────────────
            if (!string.IsNullOrWhiteSpace(input))
            {
                string preview = input.Replace("\r\n", " ").Replace('\n', ' ').Trim();
                if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";

                var inputLabel = new TextBlock
                {
                    Text         = preview,
                    FontSize     = 11,
                    FontFamily   = new FontFamily("Consolas"),
                    Foreground   = new SolidColorBrush(ThemeService.Current.TextSecondary),
                    TextWrapping = TextWrapping.Wrap,
                    Margin       = new Thickness(16, 2, 0, 0),
                    Opacity      = 0.75
                };
                outerStack.Children.Add(inputLabel);
            }

            border.Child = outerStack;

            var parent = _currentAssistantBlock?.Parent as StackPanel;
            parent?.Children.Add(border);

            // Register the bubble state by toolId
            if (!string.IsNullOrEmpty(toolId))
            {
                _toolBubbles[toolId] = new ToolBubbleState
                {
                    IconLabel   = iconLabel,
                    StatusLabel = statusLabel,
                    StartTime   = DateTime.Now
                };
            }

            ScrollToBottom();
        }

        /// <summary>Start the animated dots on a tool bubble (call when execution begins).</summary>
        public void StartToolAnimation(string toolId)
        {
            if (string.IsNullOrEmpty(toolId) || !_toolBubbles.TryGetValue(toolId, out var state))
                return;

            state.StartTime = DateTime.Now;
            state.IconLabel.Text = "⚙️";

            int frame = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (s, e) =>
            {
                state.StatusLabel.Text = DotFrames[frame % DotFrames.Length];
                frame++;
            };
            timer.Start();
            state.Timer = timer;
            state.StatusLabel.Text = DotFrames[0];
        }

        /// <summary>Finalize a tool bubble with success or failure (call when execution ends).</summary>
        public void CompleteToolBubble(string toolId, bool success)
        {
            if (string.IsNullOrEmpty(toolId) || !_toolBubbles.TryGetValue(toolId, out var state))
                return;

            state.Timer?.Stop();
            state.Timer = null;

            double elapsed = (DateTime.Now - state.StartTime).TotalSeconds;

            if (success)
            {
                state.IconLabel.Text   = "✅";
                state.StatusLabel.Text = $"{elapsed:F1}s";
                state.StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            }
            else
            {
                state.IconLabel.Text   = "❌";
                state.StatusLabel.Text = "失敗";
                state.StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
            }

            _toolBubbles.Remove(toolId);
        }

        public void ShowToolResultsDivider()
        {
            EnsureAssistantContainer();
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(ThemeService.Current.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 8, 0, 8)
            };

            var parent = _currentAssistantBlock?.Parent as StackPanel;
            parent?.Children.Add(border);
        }

        public void AddAssistantMessage(string content, List<ToolCall> toolCalls = null)
        {
            StartAssistantMessage();
            AppendAssistantText(content ?? "");
            if (toolCalls != null)
                foreach (var tc in toolCalls)
                    ShowToolCall(tc.Name, tc.Id, tc.Arguments?.ToString());
            FinalizeAssistantMessage();
        }

        public void FinalizeAssistantMessage()
        {
            // Re-render with syntax highlighting if text contains code fences
            ApplyMarkdownHighlighting();

            // Collapse thinking block and update header to show char count
            if (_thinkingHeaderLabel != null && _currentThinkingBlock != null)
            {
                var charCount = _currentThinkingBlock.Text.Length;
                _thinkingHeaderLabel.Text = $"💭 思考過程 · {charCount:N0} 字";
                _thinkingHeaderLabel.FontStyle = FontStyles.Normal;
                if (_thinkingContent != null)
                    _thinkingContent.Visibility = Visibility.Collapsed;
                if (_thinkingArrow != null)
                    _thinkingArrow.Text = " ▼";
            }

            _currentAssistantBlock  = null;
            _currentThinkingBlock   = null;
            _thinkingContainer      = null;
            _thinkingContent        = null;
            _thinkingHeaderLabel    = null;
            _thinkingArrow          = null;
            _assistantContainerCreated = false;
            RemoveTypingBubble();
            ThinkingIndicator.Visibility = Visibility.Collapsed;
            SetSendEnabled(true);
            ScrollToBottom();
        }

        private void ApplyMarkdownHighlighting()
        {
            if (_currentAssistantBlock == null) return;
            var fullText = _currentAssistantBlock.Text;
            if (!MarkdownRenderer.HasCodeBlock(fullText)) return;

            var parent = _currentAssistantBlock.Parent as StackPanel;
            if (parent == null) return;

            var idx = parent.Children.IndexOf(_currentAssistantBlock);
            parent.Children.Remove(_currentAssistantBlock);

            var elements = MarkdownRenderer.Render(fullText, _msgFontSize, _msgFontFamily);
            foreach (var el in elements)
                parent.Children.Insert(idx++, el);
        }

        public void ShowError(string error)
        {
            var tb = new TextBlock
            {
                Text = $"⚠ {error}",
                Foreground = new SolidColorBrush(ThemeService.Current.Error),
                FontSize = 12, Margin = new Thickness(0, 4, 0, 4)
            };
            MessagesPanel.Children.Add(tb);
            ScrollToBottom();
        }

        /// <summary>
        /// 顯示系統/指令回覆訊息（用於 slash command 輸出）。
        /// 以帶有背景的方塊呈現，與一般 AI 對話區隔。
        /// </summary>
        public void ShowSystemMessage(string text, bool isError = false,
            double fontSize = 12, string fontFamily = "Consolas, Cascadia Code, Courier New")
        {
            var bg    = isError
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 255, 80, 80))
                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 100, 180, 255));
            var fg    = isError
                ? new SolidColorBrush(ThemeService.Current.Error)
                : new SolidColorBrush(ThemeService.Current.TextSecondary);

            var border = new Border
            {
                Background    = bg,
                CornerRadius  = new CornerRadius(6),
                Padding       = new Thickness(10, 8, 10, 8),
                Margin        = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            };
            var tb = new TextBlock
            {
                Text         = text,
                Foreground   = fg,
                FontSize     = fontSize,
                FontFamily   = new System.Windows.Media.FontFamily(fontFamily),
                TextWrapping = System.Windows.TextWrapping.Wrap,
            };
            border.Child = tb;
            MessagesPanel.Children.Add(border);
            ScrollToBottom();
        }

        public void SetSendEnabled(bool enabled)
        {
            _vm.IsSending = !enabled;
            InputBox.IsEnabled = enabled;

            if (enabled)
            {
                RemoveTypingBubble();
                _vm.NotifySendComplete();
                InputBox.Focus();
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                if (_vm.SendCommand.CanExecute(null))
                    _vm.SendCommand.Execute(null);
            }
        }

        // ── File attachment handlers ───────────────────────────────────────

        private void AttachFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "選擇要附加的檔案",
                Multiselect = true,
                Filter = "支援的檔案|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt;*.md;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.csv"
                       + "|PDF 文件|*.pdf"
                       + "|Word 文件|*.doc;*.docx"
                       + "|Excel 試算表|*.xls;*.xlsx"
                       + "|PowerPoint 簡報|*.ppt;*.pptx"
                       + "|圖片|*.png;*.jpg;*.jpeg;*.gif;*.bmp"
                       + "|文字檔|*.txt;*.md;*.csv"
                       + "|所有檔案|*.*"
            };

            if (dlg.ShowDialog() == true)
                _vm.AddFiles(dlg.FileNames);
        }

        private void RemoveFile_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is AttachedFileInfo file)
                _vm.RemoveFile(file);
        }

        // ── Typing bubble (animated dots while waiting for first response token) ──

        private void ShowTypingBubble()
        {
            if (_typingBubble != null) return; // already showing

            var dot1 = MakeDot(1.0);
            var dot2 = MakeDot(0.3);
            var dot3 = MakeDot(0.3);
            _typingDots = new[] { dot1, dot2, dot3 };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(dot1);
            row.Children.Add(dot2);
            row.Children.Add(dot3);

            _typingBubble = new Border
            {
                Background = new SolidColorBrush(ThemeService.Current.AssistantBubble),
                CornerRadius = new CornerRadius(12, 12, 12, 2),
                Margin = new Thickness(0, 4, 40, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = row
            };

            MessagesPanel.Children.Add(_typingBubble);
            ScrollToBottom();

            _typingFrame = 0;
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _typingTimer.Tick += (s, e) =>
            {
                _typingFrame = (_typingFrame + 1) % 3;
                for (int i = 0; i < 3; i++)
                    _typingDots[i].Opacity = (i == _typingFrame) ? 1.0 : 0.25;
            };
            _typingTimer.Start();
        }

        private static Ellipse MakeDot(double opacity) => new Ellipse
        {
            Width = 9, Height = 9,
            Fill = new SolidColorBrush(ThemeService.Current.Accent),
            Opacity = opacity,
            Margin = new Thickness(0, 0, 6, 0)
        };

        private void RemoveTypingBubble()
        {
            _typingTimer?.Stop();
            _typingTimer = null;
            if (_typingBubble != null)
            {
                MessagesPanel.Children.Remove(_typingBubble);
                _typingBubble = null;
            }
            _typingDots = null;
        }

        private void ScrollToBottom()
        {
            MessagesScroll.ScrollToBottom();
        }
    }
}
