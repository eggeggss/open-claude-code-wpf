using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace OpenClaudeCodeWPF.Services
{
    public class ThemeColors
    {
        public string Name          { get; set; }
        public Color PrimaryBg      { get; set; }
        public Color SecondaryBg    { get; set; }
        public Color TertiaryBg     { get; set; }
        public Color InputBoxBg     { get; set; }
        public Color Border         { get; set; }
        public Color TextPrimary    { get; set; }
        public Color TextSecondary  { get; set; }
        public Color Accent         { get; set; }
        public Color UserBubble     { get; set; }
        public Color AssistantBubble{ get; set; }
        public Color ToolCallBg     { get; set; }
        public Color Error          { get; set; }
        public Color Success        { get; set; }
        public Color TitleBar       { get; set; }
        public Color StatusBar      { get; set; }
        public Color SendButton     { get; set; }
    }

    public static class ThemeService
    {
        private static readonly Dictionary<string, ThemeColors> _themes =
            new Dictionary<string, ThemeColors>
        {
            ["Dark"] = new ThemeColors
            {
                Name           = "🌙 深色背景",
                PrimaryBg      = H("#1E1E1E"),
                SecondaryBg    = H("#252526"),
                TertiaryBg     = H("#2D2D30"),
                InputBoxBg     = H("#3C3C3C"),
                Border         = H("#3E3E42"),
                TextPrimary    = H("#CCCCCC"),
                TextSecondary  = H("#858585"),
                Accent         = H("#7C6AFF"),
                UserBubble     = H("#2B2B3D"),
                AssistantBubble= H("#1E1E2E"),
                ToolCallBg     = H("#1A2E1A"),
                Error          = H("#F44747"),
                Success        = H("#4EC9B0"),
                TitleBar       = H("#323233"),
                StatusBar      = H("#007ACC"),
                SendButton     = H("#7C6AFF"),
            },
            ["Light"] = new ThemeColors
            {
                Name           = "☀️ 白色背景",
                PrimaryBg      = H("#F5F5F5"),
                SecondaryBg    = H("#E8E8E8"),
                TertiaryBg     = H("#DCDCDC"),
                InputBoxBg     = H("#FFFFFF"),
                Border         = H("#CCCCCC"),
                TextPrimary    = H("#1E1E1E"),
                TextSecondary  = H("#6E6E6E"),
                Accent         = H("#5C6BC0"),
                UserBubble     = H("#D4E4FF"),
                AssistantBubble= H("#ECECEC"),
                ToolCallBg     = H("#E0F4E0"),
                Error          = H("#D32F2F"),
                Success        = H("#2E7D32"),
                TitleBar       = H("#E0E0E0"),
                StatusBar      = H("#1976D2"),
                SendButton     = H("#5C6BC0"),
            },
            // Open Claude Code WPF：深黑底 + Anthropic 珊瑚橘主調，加入微妙的橘色光暈感
            ["ClaudeCode"] = new ThemeColors
            {
                Name           = "🤖 Open Claude Code WPF",
                PrimaryBg      = H("#0C0A08"),   // 帶暖色調的極深黑
                SecondaryBg    = H("#1A1410"),   // 橘調深棕黑（使用者訊息區）
                TertiaryBg     = H("#251C14"),   // 略亮的橘調背景
                InputBoxBg     = H("#130F0B"),   // 輸入框深黑
                Border         = H("#3D2B1A"),   // 橘調邊框，不刺眼
                TextPrimary    = H("#F5EDE4"),   // 帶暖意的純白，護眼
                TextSecondary  = H("#A08070"),   // 橘調灰，有質感
                Accent         = H("#FF6000"),   // 更鮮豔的 Claude 橘
                UserBubble     = H("#2A1E13"),   // 深橘棕使用者氣泡
                AssistantBubble= H("#0C0A08"),   // 與背景同色，無框感
                ToolCallBg     = H("#1A1008"),   // 工具呼叫深橘黑
                Error          = H("#FF5F6D"),   // 珊瑚紅錯誤
                Success        = H("#3DBA7A"),   // 翠綠成功
                TitleBar       = H("#060504"),   // 幾乎純黑標題列
                StatusBar      = H("#2D1800"),   // 暗橘狀態列底色
                SendButton     = H("#FF6000"),   // 橘色發送按鈕
            },
        };

        public static ThemeColors Current { get; private set; } = _themes["Light"];
        public static string CurrentName  { get; private set; } = "Light";

        public static event EventHandler ThemeChanged;

        public static IEnumerable<KeyValuePair<string, ThemeColors>> GetAllThemes()
            => _themes;

        public static void Apply(string themeName)
        {
            if (string.IsNullOrEmpty(themeName) || !_themes.ContainsKey(themeName))
                themeName = "Dark";

            var t = _themes[themeName];
            Current     = t;
            CurrentName = themeName;

            Brush("PrimaryBgBrush",      t.PrimaryBg);
            Brush("SecondaryBgBrush",    t.SecondaryBg);
            Brush("TertiaryBgBrush",     t.TertiaryBg);
            Brush("InputBoxBrush",       t.InputBoxBg);
            Brush("BorderBrush",         t.Border);
            Brush("PrimaryTextBrush",    t.TextPrimary);
            Brush("SecondaryTextBrush",  t.TextSecondary);
            Brush("AccentBrush",         t.Accent);
            Brush("UserMsgBgBrush",      t.UserBubble);
            Brush("AssistantMsgBgBrush", t.AssistantBubble);
            Brush("ToolBgBrush",         t.ToolCallBg);
            Brush("ErrorBrush",          t.Error);
            Brush("SuccessBrush",        t.Success);
            Brush("TitleBarBrush",       t.TitleBar);
            Brush("StatusBarBrush",      t.StatusBar);
            Brush("SendButtonBrush",     t.SendButton);

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void Brush(string key, Color color)
        {
            if (Application.Current.Resources[key] is SolidColorBrush b && !b.IsFrozen)
                b.Color = color;
            else
                Application.Current.Resources[key] = new SolidColorBrush(color);
        }

        private static Color H(string hex)
            => (Color)ColorConverter.ConvertFromString(hex);
    }
}
