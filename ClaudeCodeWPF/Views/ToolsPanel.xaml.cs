using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

            // ── Skills (future extension point) ──────────────────
            // For now, derive "skills" from tools that are marked as higher-level.
            var skillNames = new[] { "Agent", "WebSearch" };
            var skills = tools.Where(t => skillNames.Contains(t.Name)).ToList();
            SkillsHeader.Text = $"⚡ 技能  ({skills.Count})";

            if (skills.Count == 0)
            {
                SkillsPanel.Children.Add(new TextBlock
                {
                    Text = "目前無額外技能",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    FontSize = 11, Margin = new Thickness(12, 4, 8, 4)
                });
            }
            else
            {
                foreach (var skill in skills.OrderBy(t => t.Name))
                {
                    var card = BuildToolCard(skill.Name, skill.Description, isSkill: true);
                    SkillsPanel.Children.Add(card);
                }
            }
        }

        private Border BuildToolCard(string name, string description, bool isSkill)
        {
            var cat = _categories.ContainsKey(name) ? _categories[name] : DefaultCategory;
            var badgeBg   = (Color)ColorConverter.ConvertFromString(cat[0]);
            var badgeFg   = (Color)ColorConverter.ConvertFromString(cat[1]);
            var catLabel  = cat[2];

            // Truncate long descriptions
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

            // Row 1: name + badge
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

            // Row 2: description
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
