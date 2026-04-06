using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenClaudeCodeWPF.Views
{
    public partial class ToolOutputPanel : UserControl
    {
        public ToolOutputPanel()
        {
            InitializeComponent();
        }

        public void AddToolStart(string toolName, string input)
        {
            var border = CreateBorder("#1A1A2E", "#4455AA");
            var stack = new StackPanel();
            stack.Children.Add(CreateLabel($"▶ {toolName}", "#88BBFF"));
            if (!string.IsNullOrEmpty(input) && input.Length < 500)
                stack.Children.Add(CreateCode(input, "#AAAAAA"));
            border.Child = stack;
            OutputPanel.Children.Add(border);
            OutputScroll.ScrollToBottom();
        }

        public void AddToolResult(string toolName, string result)
        {
            var border = CreateBorder("#1A2E1A", "#44AA55");
            var stack = new StackPanel();
            stack.Children.Add(CreateLabel($"✓ {toolName}", "#88FF88"));
            if (!string.IsNullOrEmpty(result) && result.Length < 2000)
                stack.Children.Add(CreateCode(result, "#CCCCCC"));
            border.Child = stack;
            OutputPanel.Children.Add(border);
            OutputScroll.ScrollToBottom();
        }

        public void AddToolError(string toolName, string error)
        {
            var border = CreateBorder("#2E1A1A", "#AA4455");
            var stack = new StackPanel();
            stack.Children.Add(CreateLabel($"✗ {toolName}", "#FF8888"));
            stack.Children.Add(CreateCode(error, "#FF6666"));
            border.Child = stack;
            OutputPanel.Children.Add(border);
            OutputScroll.ScrollToBottom();
        }

        public void Clear()
        {
            OutputPanel.Children.Clear();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e) => Clear();

        private Border CreateBorder(string bg, string border)
        {
            return new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border)),
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(8, 4, 8, 4)
            };
        }

        private TextBlock CreateLabel(string text, string color)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                FontSize = 12, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas")
            };
        }

        private TextBox CreateCode(string text, string color)
        {
            return new TextBox
            {
                Text = text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 11, FontFamily = new FontFamily("Consolas"),
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                MaxHeight = 200
            };
        }
    }
}
