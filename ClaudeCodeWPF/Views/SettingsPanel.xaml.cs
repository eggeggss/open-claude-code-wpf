using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenClaudeCodeWPF.ViewModels;

namespace OpenClaudeCodeWPF.Views
{
    public partial class SettingsPanel : Window
    {
        private readonly SettingsViewModel _vm;

        public SettingsPanel()
        {
            InitializeComponent();
            _vm = new SettingsViewModel();
            DataContext = _vm;

            // Highlight the active theme button once on load
            HighlightActiveThemeButton(_vm.CurrentTheme);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.Save();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string themeName)
            {
                _vm.ApplyTheme(themeName);
                HighlightActiveThemeButton(themeName);
            }
        }

        private void HighlightActiveThemeButton(string activeTheme)
        {
            var buttons = new[] { ThemeBtn_Dark, ThemeBtn_Light, ThemeBtn_ClaudeCode };
            foreach (var btn in buttons)
            {
                bool active = (btn.Tag as string) == activeTheme;
                btn.BorderThickness = new Thickness(active ? 2 : 0);
                btn.BorderBrush     = active
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x60, 0x00))
                    : null;
            }
        }
    }
}

