using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.Skills;
using OpenClaudeCodeWPF.Services.ToolSystem;

namespace OpenClaudeCodeWPF.Views
{
    public partial class ToolsPanel : UserControl
    {
        // Emitted when user clicks a tool card — lets ChatPanel insert a usage hint
        public event Action<string, string> OnToolClicked;

        // Category colours
        private static readonly Dictionary<string, string[]> _categories = new Dictionary<string, string[]>
        {
            { "PowerShell", new[] { "#2B6CB0", "#63B3ED", "系統" } },
            { "Bash",       new[] { "#276749", "#68D391", "系統" } },
            { "FileRead",   new[] { "#744210", "#F6AD55", "檔案" } },
            { "FileWrite",  new[] { "#744210", "#F6AD55", "檔案" } },
            { "FileEdit",   new[] { "#744210", "#F6AD55", "檔案" } },
            { "Grep",       new[] { "#553C9A", "#B794F4", "搜尋" } },
            { "Glob",       new[] { "#553C9A", "#B794F4", "搜尋" } },
            { "WebFetch",   new[] { "#1A365D", "#76E4F7", "網路" } },
            { "WebSearch",  new[] { "#1A365D", "#76E4F7", "網路" } },
            { "OpenEdge",   new[] { "#1A365D", "#76E4F7", "瀏覽器" } },
            { "Agent",      new[] { "#702459", "#FBB6CE", "代理" } },
        };

        private static string[] DefaultCategory => new[] { "#2D3748", "#A0AEC0", "工具" };

        public ToolsPanel()
        {
            InitializeComponent();
            SkillService.Instance.SkillsChanged += () => Dispatcher.Invoke(Refresh);
            Loaded += (s, e) => Refresh();
        }

        public void Refresh()
        {
            McpToolsPanel.Children.Clear();
            SkillsPanel.Children.Clear();

            // ── MCP / built-in tools ─────────────────────────────
            var tools = ToolRegistry.Instance.GetAllTools();
            McpHeader.Text = $"🔧 MCP 工具  ({tools.Count})";

            foreach (var tool in tools.OrderBy(t => t.Name))
            {
                var card = BuildToolCard(tool.Name, tool.Description, isSkill: false);
                McpToolsPanel.Children.Add(card);
            }

            // ── Custom Skills ─────────────────────────────────────
            var skills = SkillService.Instance.AllSkills;
            SkillsHeader.Text = $"⚡ 技能  ({skills.Count})";

            if (skills.Count == 0)
            {
                SkillsPanel.Children.Add(new TextBlock
                {
                    Text = "尚無自訂技能。\n點擊「＋ 上傳」新增 .json 技能檔案。",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 11,
                    Margin = new Thickness(12, 4, 8, 4),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else
            {
                foreach (var skill in skills)
                    SkillsPanel.Children.Add(BuildSkillCard(skill));
            }
        }

        // ── Skill card ────────────────────────────────────────────────────────

        private Border BuildSkillCard(SkillDefinition skill)
        {
            bool isActive = SkillService.Instance.ActiveSkill?.Name == skill.Name;

            var cardBorder = new Border
            {
                Background = isActive
                    ? new SolidColorBrush(Color.FromRgb(0x1A, 0x2A, 0x1A))
                    : new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                BorderBrush = isActive
                    ? new SolidColorBrush(Color.FromRgb(0x40, 0xA0, 0x40))
                    : new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(6, 2, 6, 2),
                Padding = new Thickness(8, 8, 8, 8)
            };

            var stack = new StackPanel();

            // Row 1: icon + name + active badge
            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };

            titleRow.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(skill.Icon) ? "⚡" : skill.Icon,
                FontSize = 14,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            titleRow.Children.Add(new TextBlock
            {
                Text = skill.Name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            });

            if (!string.IsNullOrEmpty(skill.Version))
            {
                titleRow.Children.Add(new TextBlock
                {
                    Text = $"v{skill.Version}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 9,
                    Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            if (isActive)
            {
                titleRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x20, 0x55, 0x20)),
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(7, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "✓ 啟用中",
                        Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xD0, 0x60)),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold
                    }
                });
            }

            stack.Children.Add(titleRow);

            // Row 2: description
            if (!string.IsNullOrWhiteSpace(skill.Description))
            {
                var desc = skill.Description.Length > 120
                    ? skill.Description.Substring(0, 117) + "…"
                    : skill.Description;

                stack.Children.Add(new TextBlock
                {
                    Text = desc,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Segoe UI"),
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }

            // Row 3: action buttons
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };

            var toggleBtn = new Button
            {
                Content = isActive ? "停用" : "啟用",
                FontSize = 10,
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 0),
                Background = isActive
                    ? new SolidColorBrush(Color.FromRgb(0x30, 0x60, 0x30))
                    : new SolidColorBrush(Color.FromRgb(0x1A, 0x4A, 0x7A)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = isActive ? "停用此技能" : "啟用此技能（會附加到系統提示詞）"
            };

            toggleBtn.Click += (s, e) =>
            {
                if (isActive)
                    SkillService.Instance.DeactivateSkill();
                else
                    SkillService.Instance.ActivateSkill(skill);
                // Refresh is triggered via SkillsChanged event
            };

            var deleteBtn = new Button
            {
                Content = "🗑 刪除",
                FontSize = 10,
                Padding = new Thickness(8, 3, 8, 3),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x40, 0x40)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x30, 0x30)),
                Cursor = Cursors.Hand,
                ToolTip = "從磁碟中刪除此技能檔案"
            };

            deleteBtn.Click += (s, e) =>
            {
                var result = MessageBox.Show(
                    $"確定要刪除技能「{skill.Name}」？\n\n此操作將從磁碟中移除技能檔案，無法復原。",
                    "刪除技能",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                    SkillService.Instance.DeleteSkill(skill);
            };

            btnRow.Children.Add(toggleBtn);
            btnRow.Children.Add(deleteBtn);

            // "在資料夾中顯示" link
            var openFolderBtn = new Button
            {
                Content = "📁",
                FontSize = 11,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "在檔案總管中顯示技能資料夾"
            };
            openFolderBtn.Click += (s, e) =>
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
            };
            btnRow.Children.Add(openFolderBtn);

            stack.Children.Add(btnRow);
            cardBorder.Child = stack;
            return cardBorder;
        }

        // ── Upload handler ────────────────────────────────────────────────────

        private void ImportSkillButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "選擇技能檔案",
                Filter = "技能檔案 (*.json)|*.json|所有檔案 (*.*)|*.*",
                DefaultExt = ".json"
            };

            if (dlg.ShowDialog() != true) return;

            var error = SkillService.Instance.ImportSkill(dlg.FileName);
            if (error == null)
            {
                MessageBox.Show(
                    $"✓ 技能已成功匯入！\n\n技能已儲存至：\n{SkillService.SkillsDir}",
                    "匯入成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"❌ 匯入失敗\n\n{error}",
                    "匯入失敗",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ── Tool card (existing) ──────────────────────────────────────────────

        private Border BuildToolCard(string name, string description, bool isSkill)
        {
            var cat = _categories.ContainsKey(name) ? _categories[name] : DefaultCategory;
            var badgeBg   = (Color)ColorConverter.ConvertFromString(cat[0]);
            var badgeFg   = (Color)ColorConverter.ConvertFromString(cat[1]);
            var catLabel  = cat[2];

            var shortDesc = description?.Length > 120
                ? description.Substring(0, 117) + "…"
                : description ?? "";

            var cardBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(6, 2, 6, 2),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand,
                ToolTip = description
            };

            cardBorder.MouseEnter += (s, e) =>
                cardBorder.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2E));
            cardBorder.MouseLeave += (s, e) =>
                cardBorder.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
            cardBorder.MouseLeftButtonUp += (s, e) =>
                OnToolClicked?.Invoke(name, description);

            var stack = new StackPanel();

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };

            titleRow.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12, FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            });

            titleRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(badgeBg),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = catLabel,
                    Foreground = new SolidColorBrush(badgeFg),
                    FontSize = 9, FontWeight = FontWeights.Bold
                }
            });

            stack.Children.Add(titleRow);

            stack.Children.Add(new TextBlock
            {
                Text = shortDesc,
                Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                FontSize = 11, TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe UI")
            });

            cardBorder.Child = stack;
            return cardBorder;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();
    }
}
