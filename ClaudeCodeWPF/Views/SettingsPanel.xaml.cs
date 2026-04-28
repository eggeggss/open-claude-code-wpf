using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.Skills;
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

            HighlightActiveThemeButton(_vm.CurrentTheme);
            SkillService.Instance.SkillsChanged += () => Dispatcher.Invoke(RefreshSkillList);
            Loaded += (s, e) => RefreshSkillList();
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

        // ── Skills Tab ────────────────────────────────────────────────────────

        /// <summary>Wrapper for ListBox binding — holds display state</summary>
        private class SkillItem
        {
            public SkillDefinition Skill     { get; set; }
            public string  Icon             { get; set; }
            public string  Name             { get; set; }
            public string  ShortDescription { get; set; }
            public bool    IsActive         { get; set; }
            public string  ActiveColor      => IsActive ? "#40C040" : "#444444";
            public string  ActiveTooltip    => IsActive ? "啟用中" : "未啟用";
        }

        private void RefreshSkillList()
        {
            var selected = (SkillListBox.SelectedItem as SkillItem)?.Skill?.Name;

            SkillListBox.Items.Clear();
            foreach (var skill in SkillService.Instance.AllSkills)
            {
                var item = new SkillItem
                {
                    Skill            = skill,
                    Icon             = string.IsNullOrEmpty(skill.Icon) ? "⚡" : skill.Icon,
                    Name             = skill.Name,
                    ShortDescription = skill.Description?.Length > 60
                                       ? skill.Description.Substring(0, 57) + "…"
                                       : skill.Description ?? "",
                    IsActive = SkillService.Instance.IsSkillActive(skill)
                };
                SkillListBox.Items.Add(item);

                // Re-select previously selected item
                if (skill.Name == selected)
                    SkillListBox.SelectedItem = item;
            }

            if (SkillListBox.SelectedItem == null)
                ShowSkillDetail(null);
        }

        private void SkillListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = SkillListBox.SelectedItem as SkillItem;
            ShowSkillDetail(item?.Skill);
        }

        private void ShowSkillDetail(SkillDefinition skill)
        {
            if (skill == null)
            {
                SkillDetailEmpty.Visibility   = Visibility.Visible;
                SkillDetailContent.Visibility = Visibility.Collapsed;
                return;
            }

            SkillDetailEmpty.Visibility   = Visibility.Collapsed;
            SkillDetailContent.Visibility = Visibility.Visible;

            bool isActive = SkillService.Instance.IsSkillActive(skill);

            SkillDetailIcon.Text  = string.IsNullOrEmpty(skill.Icon) ? "⚡" : skill.Icon;
            SkillDetailName.Text  = skill.Name;

            var meta = "";
            if (!string.IsNullOrEmpty(skill.Version)) meta += $"v{skill.Version}";
            if (!string.IsNullOrEmpty(skill.Author))  meta += (meta.Length > 0 ? "  ·  " : "") + skill.Author;
            SkillDetailMeta.Text = meta;

            SkillDetailDesc.Text = string.IsNullOrEmpty(skill.Description)
                ? "（無說明）" : skill.Description;

            SkillDetailPromptPreview.Text = skill.SystemPrompt ?? "";

            if (isActive)
            {
                SkillActivateBtn.Content    = "⏹ 停用";
                SkillActivateBtn.Background = new SolidColorBrush(Color.FromRgb(0x30, 0x60, 0x30));
                SkillActivateBtn.ToolTip    = "停用此技能";
            }
            else
            {
                SkillActivateBtn.Content    = "▶ 啟用";
                SkillActivateBtn.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x4A, 0x7A));
                SkillActivateBtn.ToolTip    = "啟用此技能（附加至系統提示詞）";
            }
        }

        private void SkillActivateBtn_Click(object sender, RoutedEventArgs e)
        {
            var item = SkillListBox.SelectedItem as SkillItem;
            if (item == null) return;

            bool isActive = SkillService.Instance.IsSkillActive(item.Skill);
            if (isActive)
                SkillService.Instance.DeactivateSkill(item.Skill);
            else
                SkillService.Instance.ActivateSkill(item.Skill);
            // RefreshSkillList triggered by SkillsChanged event
        }

        private void SkillDeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            var item = SkillListBox.SelectedItem as SkillItem;
            if (item == null) return;

            var result = MessageBox.Show(
                $"確定要刪除技能「{item.Skill.Name}」？\n\n此操作將從磁碟中移除技能檔案，無法復原。",
                "刪除技能", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                var error = SkillService.Instance.DeleteSkill(item.Skill);
                if (error != null)
                    MessageBox.Show($"❌ 刪除失敗\n\n{error}",
                        "刪除失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportSkillSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title      = "選擇技能檔案",
                Filter     = "技能檔案 (*.json;*.zip;*.md)|*.json;*.zip;*.md|JSON (*.json)|*.json|ZIP (*.zip)|*.zip|SKILL.md (*.md)|*.md|所有檔案 (*.*)|*.*",
                DefaultExt = ".json"
            };

            if (dlg.ShowDialog() != true) return;

            var error = SkillService.Instance.ImportSkill(dlg.FileName);
            if (error == null)
            {
                MessageBox.Show(
                    $"✓ 技能已成功匯入！",
                    "匯入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"❌ 匯入失敗\n\n{error}",
                    "匯入失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSkillFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SkillService.SkillsDir,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}

