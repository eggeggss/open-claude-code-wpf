using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using OpenClaudeCodeWPF.Services;

namespace OpenClaudeCodeWPF.Utils
{
    /// <summary>
    /// Parses markdown code fences (```lang\n…```) in assistant replies and
    /// renders them as WPF panels with syntax highlighting.
    /// Plain text → TextBox  |  Code fence → styled Border with TextBlock runs.
    /// </summary>
    public static class MarkdownRenderer
    {
        // ── Regexes ────────────────────────────────────────────────────────────
        // Matches ```[lang]\n … \n``` (non-greedy)
        private static readonly Regex FenceRe = new Regex(
            "```([^\\n`]*)\\n([\\s\\S]*?)```",
            RegexOptions.Compiled);

        // Syntax token groups: 1=comment, 2=string, 3=number, 4=keyword
        private static readonly Regex TokenRe = new Regex(
            "(//[^\\n]*|/\\*[\\s\\S]*?\\*/|#[^\\n]*)"          // comment
          + "|(\"(?:[^\"\\\\\\n]|\\\\.)*\"|'(?:[^'\\\\\\n]|\\\\.)*'|`[^`]*`)"  // string
          + "|\\b(0x[\\da-fA-F]+|\\d+\\.?\\d*[fFdDmM]?)\\b"   // number
          + "|\\b(abstract|as|async|await|base|bool|break|byte|case|catch|char|class|const|"
          + "continue|decimal|default|delegate|do|double|else|enum|event|extern|false|"
          + "finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|"
          + "is|lock|long|namespace|new|null|object|operator|out|override|params|private|"
          + "protected|public|readonly|ref|return|sbyte|sealed|short|sizeof|static|string|"
          + "struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|"
          + "using|var|virtual|void|volatile|while|"
          + "def|elif|except|from|import|lambda|pass|raise|with|yield|"
          + "let|function|fn|impl|trait|pub|mod|match|println|"
          + "SELECT|FROM|WHERE|INSERT|UPDATE|DELETE|CREATE|DROP|TABLE|JOIN|"
          + "ON|GROUP|ORDER|BY|HAVING|AND|OR|NOT|NULL|IS|IN|LIKE|BETWEEN|"
          + "WHEN|THEN|END|val|fun|data|companion)\\b",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // ── Token colors (VS Code dark theme — readable regardless of app theme) ──
        private static readonly Color ColKeyword = C("#569CD6");
        private static readonly Color ColString  = C("#CE9178");
        private static readonly Color ColComment = C("#6A9955");
        private static readonly Color ColNumber  = C("#B5CEA8");
        private static readonly Color ColDefault = C("#D4D4D4");
        private static readonly Color ColCodeBg  = C("#1E1E1E");
        private static readonly Color ColHeader  = C("#252526");
        private static readonly Color ColBorder  = C("#454545");
        private static readonly Color ColLangLbl = C("#858585");

        // Heuristic: detects bare code blocks (AI forgot to use fences)
        // Must have 4+ consecutive lines that look code-like
        private static readonly Regex HeuristicCodeRe = new Regex(
            @"((?:[ \t]*(?:using |namespace |class |public |private |protected |static |void |int |string |var |async |await |function |def |import |from |#include |foreach |for\(|if\(|return |Console\.|System\.|Task\.|List<|Dictionary<)[^\n]+\n){4,})",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // ── Public API ─────────────────────────────────────────────────────────

        public static bool HasCodeBlock(string text)
        {
            if (text == null) return false;
            if (text.Contains("```")) return true;
            return HeuristicCodeRe.IsMatch(text);
        }

        /// <summary>
        /// Splits <paramref name="text"/> into WPF UIElements.
        /// Handles explicit ``` fences and heuristic detection for bare code.
        /// </summary>
        public static List<UIElement> Render(string text, double fontSize, FontFamily fontFamily)
        {
            var result = new List<UIElement>();
            if (string.IsNullOrEmpty(text)) return result;

            if (text.Contains("```"))
            {
                // Explicit fence mode
                int pos = 0;
                foreach (Match m in FenceRe.Matches(text))
                {
                    if (m.Index > pos)
                    {
                        var prose = text.Substring(pos, m.Index - pos).Trim('\n');
                        if (!string.IsNullOrWhiteSpace(prose))
                            result.Add(MakeProseBox(prose, fontSize, fontFamily));
                    }
                    result.Add(MakeCodePanel(m.Groups[2].Value, m.Groups[1].Value.Trim(), fontSize));
                    pos = m.Index + m.Length;
                }
                if (pos < text.Length)
                {
                    var tail = text.Substring(pos).Trim('\n');
                    if (!string.IsNullOrWhiteSpace(tail))
                        result.Add(MakeProseBox(tail, fontSize, fontFamily));
                }
            }
            else
            {
                // Heuristic fallback: detect bare code blocks
                int pos = 0;
                foreach (Match m in HeuristicCodeRe.Matches(text))
                {
                    if (m.Index > pos)
                    {
                        var prose = text.Substring(pos, m.Index - pos).Trim('\n');
                        if (!string.IsNullOrWhiteSpace(prose))
                            result.Add(MakeProseBox(prose, fontSize, fontFamily));
                    }
                    result.Add(MakeCodePanel(m.Value.TrimEnd(), DetectLang(m.Value), fontSize));
                    pos = m.Index + m.Length;
                }
                if (pos < text.Length)
                {
                    var tail = text.Substring(pos).Trim('\n');
                    if (!string.IsNullOrWhiteSpace(tail))
                        result.Add(MakeProseBox(tail, fontSize, fontFamily));
                }
            }

            return result;
        }

        private static string DetectLang(string code)
        {
            if (code.Contains("using System") || code.Contains("namespace ") || code.Contains("class ")) return "csharp";
            if (code.Contains("def ") && code.Contains(":")) return "python";
            if (code.Contains("#include") || code.Contains("std::")) return "cpp";
            if (code.Contains("function ") || code.Contains("const ") || code.Contains("=>")) return "javascript";
            if (code.Contains("SELECT ") || code.Contains("FROM ")) return "sql";
            return "code";
        }

        // ── Prose TextBox ───────────────────────────────────────────────────────

        private static UIElement MakeProseBox(string text, double fontSize, FontFamily font)
            => new TextBox
            {
                Text = text,
                Foreground = new SolidColorBrush(ThemeService.Current.TextPrimary),
                FontSize = fontSize,
                FontFamily = font,
                Tag = "msg",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 700,
                IsReadOnly = true,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.IBeam,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

        // ── Code block panel ────────────────────────────────────────────────────

        private static UIElement MakeCodePanel(string code, string lang, double fontSize)
        {
            var outer = new Border
            {
                Background  = new SolidColorBrush(ColCodeBg),
                BorderBrush = new SolidColorBrush(ColBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 6, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var root = new StackPanel();

            // ── Header ─────────────────────────────────────────────────────────
            var headerBorder = new Border
            {
                Background   = new SolidColorBrush(ColHeader),
                CornerRadius = new CornerRadius(5, 5, 0, 0)
            };

            var headerPanel = new DockPanel { LastChildFill = false };

            var langTb = new TextBlock
            {
                Text       = string.IsNullOrEmpty(lang) ? "code" : lang,
                Foreground = new SolidColorBrush(ColLangLbl),
                FontFamily = new FontFamily("Consolas"),
                FontSize   = 11,
                Padding    = new Thickness(10, 5, 0, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(langTb, Dock.Left);

            var copyBtn = MakeCopyButton(code);
            DockPanel.SetDock(copyBtn, Dock.Right);

            headerPanel.Children.Add(langTb);
            headerPanel.Children.Add(copyBtn);
            headerBorder.Child = headerPanel;
            root.Children.Add(headerBorder);

            // ── Code body ──────────────────────────────────────────────────────
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var codeTb = new TextBlock
            {
                FontFamily  = new FontFamily("Consolas, Courier New"),
                FontSize    = Math.Max(11, fontSize - 1),
                Foreground  = new SolidColorBrush(ColDefault),
                Background  = Brushes.Transparent,
                TextWrapping = TextWrapping.NoWrap,
                Padding     = new Thickness(12, 8, 12, 10)
            };

            ApplySyntaxHighlight(codeTb, code);
            scroll.Content = codeTb;
            root.Children.Add(scroll);

            outer.Child = root;
            return outer;
        }

        private static Button MakeCopyButton(string code)
        {
            var btn = new Button
            {
                Content = "⎘ 複製",
                Foreground = new SolidColorBrush(ColLangLbl),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                Padding  = new Thickness(10, 5, 10, 5),
                Cursor   = Cursors.Hand
            };

            btn.Click += (s, e) =>
            {
                try { Clipboard.SetText(code); } catch { }
                btn.Content = "✓ 已複製";
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (ts, _) => { btn.Content = "⎘ 複製"; t.Stop(); };
                t.Start();
            };

            return btn;
        }

        // ── Syntax highlight → TextBlock.Inlines ───────────────────────────────

        private static void ApplySyntaxHighlight(TextBlock tb, string code)
        {
            tb.Inlines.Clear();
            int pos = 0;
            foreach (Match m in TokenRe.Matches(code))
            {
                if (m.Index > pos)
                    tb.Inlines.Add(Run(code.Substring(pos, m.Index - pos), ColDefault));

                Color col;
                if      (m.Groups[1].Success) col = ColComment;
                else if (m.Groups[2].Success) col = ColString;
                else if (m.Groups[3].Success) col = ColNumber;
                else                          col = ColKeyword;

                tb.Inlines.Add(Run(m.Value, col));
                pos = m.Index + m.Length;
            }
            if (pos < code.Length)
                tb.Inlines.Add(Run(code.Substring(pos), ColDefault));
        }

        private static Run Run(string text, Color color)
            => new Run(text) { Foreground = new SolidColorBrush(color) };

        private static Color C(string hex)
            => (Color)ColorConverter.ConvertFromString(hex);
    }
}
