using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // Matches image URLs (http/https ending with image extension, or known image CDNs)
        private static readonly Regex ImageUrlRe = new Regex(
            @"(https?://\S+?\.(?:png|jpg|jpeg|gif|webp|svg|bmp)(?:\?\S*)?)"
          + @"|(https?://\S+/(?:img|image|images|afts/img)/\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Matches a GFM markdown table: header row + separator row + 1+ data rows
        private static readonly Regex TableRe = new Regex(
            @"(^\|[^\n]+\|\s*\r?\n\|[-:| ]+\|\s*\r?\n(?:\|[^\n]+\|\s*\r?\n?)+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // Detects table separator rows like |---|:---:|---:|
        private static readonly Regex SeparatorRowRe = new Regex(
            @"^\|[-:| ]+\|$", RegexOptions.Compiled);

        // ── Public API ─────────────────────────────────────────────────────────

        public static bool HasCodeBlock(string text)
        {
            if (text == null) return false;
            if (text.Contains("```")) return true;
            if (ImageUrlRe.IsMatch(text)) return true;
            if (TableRe.IsMatch(text)) return true;
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

        private static TextBox MakePlainTextBox(string text, double fontSize, FontFamily font)
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

        private static UIElement MakeProseBox(string text, double fontSize, FontFamily font)
        {
            if (ImageUrlRe.IsMatch(text) || TableRe.IsMatch(text))
                return MakeProseWithSpecialElements(text, fontSize, font);

            return MakePlainTextBox(text, fontSize, font);
        }

        /// <summary>Renders prose containing image URLs and/or markdown tables inline</summary>
        private static UIElement MakeProseWithSpecialElements(string text, double fontSize, FontFamily font)
        {
            var panel = new StackPanel { MaxWidth = 700 };

            // Collect all special matches (tables + image URLs) ordered by position
            var allMatches = new List<Match>();
            var matchTypes = new Dictionary<Match, string>();

            foreach (Match m in TableRe.Matches(text))
            {
                allMatches.Add(m);
                matchTypes[m] = "table";
            }
            foreach (Match m in ImageUrlRe.Matches(text))
            {
                allMatches.Add(m);
                matchTypes[m] = "image";
            }

            allMatches.Sort((a, b) => a.Index.CompareTo(b.Index));

            int pos = 0;
            foreach (var m in allMatches)
            {
                if (m.Index < pos) continue; // skip overlapping

                if (m.Index > pos)
                {
                    var chunk = text.Substring(pos, m.Index - pos).Trim('\n', '\r');
                    if (!string.IsNullOrWhiteSpace(chunk))
                        panel.Children.Add(MakePlainTextBox(chunk, fontSize, font));
                }

                if (matchTypes[m] == "table")
                    panel.Children.Add(MakeTableBlock(m.Value));
                else
                {
                    var url = m.Value.TrimEnd('.', ',', ')', ']', '。', '，');
                    panel.Children.Add(MakeImageBlock(url));
                }

                pos = m.Index + m.Length;
            }

            if (pos < text.Length)
            {
                var tail = text.Substring(pos).Trim('\n', '\r');
                if (!string.IsNullOrWhiteSpace(tail))
                    panel.Children.Add(MakePlainTextBox(tail, fontSize, font));
            }

            return panel;
        }

        /// <summary>Creates an Image control that loads from URL with error handling</summary>
        private static UIElement MakeImageBlock(string url)
        {
            var container = new Border
            {
                BorderBrush = new SolidColorBrush(C("#454545")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 8, 0, 8),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(C("#1E1E1E")),
                MaxWidth = 680,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var img = new Image
            {
                MaxWidth = 660,
                MaxHeight = 500,
                Stretch = System.Windows.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4),
                Cursor = Cursors.Hand
            };

            // Click to open in browser
            img.MouseLeftButtonDown += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            };
            img.ToolTip = "點擊在瀏覽器中開啟";

            // Load image asynchronously
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();

                bitmap.DownloadFailed += (s, e) =>
                {
                    // On failure, show URL as fallback text
                    container.Child = new TextBlock
                    {
                        Text = $"🖼 {url}",
                        Foreground = new SolidColorBrush(C("#4FC3F7")),
                        FontSize = 12,
                        Padding = new Thickness(8),
                        TextWrapping = TextWrapping.Wrap,
                        Cursor = Cursors.Hand
                    };
                };

                img.Source = bitmap;
            }
            catch
            {
                container.Child = new TextBlock
                {
                    Text = $"🖼 {url}",
                    Foreground = new SolidColorBrush(C("#4FC3F7")),
                    FontSize = 12,
                    Padding = new Thickness(8),
                    TextWrapping = TextWrapping.Wrap,
                    Cursor = Cursors.Hand
                };
                return container;
            }

            container.Child = img;
            return container;
        }

        // ── Markdown table ──────────────────────────────────────────────────────

        private static UIElement MakeTableBlock(string tableText)
        {
            var rawLines = tableText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            foreach (var l in rawLines)
            {
                var t = l.Trim();
                if (t.StartsWith("|")) lines.Add(t);
            }

            if (lines.Count < 2)
                return MakePlainTextBox(tableText, 12, new FontFamily("Segoe UI"));

            var headerCells = ParseCells(lines[0]);
            int colCount = headerCells.Count;
            if (colCount == 0)
                return MakePlainTextBox(tableText, 12, new FontFamily("Segoe UI"));

            // Skip separator row
            int dataStart = 1;
            if (dataStart < lines.Count && SeparatorRowRe.IsMatch(lines[dataStart]))
                dataStart = 2;

            var dataRows = new List<List<string>>();
            for (int i = dataStart; i < lines.Count; i++)
            {
                var cells = ParseCells(lines[i]);
                if (cells.Count > 0) dataRows.Add(cells);
            }

            var grid = new Grid();
            for (int c = 0; c < colCount; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int totalRows = 1 + dataRows.Count;
            for (int r = 0; r < totalRows; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header row
            for (int c = 0; c < colCount; c++)
            {
                var cellText = c < headerCells.Count ? headerCells[c] : "";
                var cell = MakeTableCell(cellText, isHeader: true,
                    isLastCol: c == colCount - 1, isLastRow: dataRows.Count == 0);
                Grid.SetRow(cell, 0);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }

            // Data rows
            for (int r = 0; r < dataRows.Count; r++)
            {
                var row = dataRows[r];
                bool isLast = r == dataRows.Count - 1;
                for (int c = 0; c < colCount; c++)
                {
                    var cellText = c < row.Count ? row[c] : "";
                    var cell = MakeTableCell(cellText, isHeader: false,
                        isLastCol: c == colCount - 1, isLastRow: isLast, evenRow: r % 2 == 0);
                    Grid.SetRow(cell, r + 1);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
            }

            return new Border
            {
                Child = grid,
                BorderBrush = new SolidColorBrush(C("#454545")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 8, 0, 8),
                MaxWidth = 700,
                HorizontalAlignment = HorizontalAlignment.Left,
                ClipToBounds = true
            };
        }

        private static UIElement MakeTableCell(string text, bool isHeader, bool isLastCol, bool isLastRow, bool evenRow = false)
        {
            var bgColor = isHeader ? C("#252526") : (evenRow ? C("#1E1E1E") : C("#1A1A1A"));
            var tb = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(isHeader ? Colors.White : C("#D4D4D4")),
                FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                FontSize = 12,
                Padding = new Thickness(10, 6, 10, 6),
                TextWrapping = TextWrapping.Wrap,
                MinWidth = 60
            };
            return new Border
            {
                Child = tb,
                Background = new SolidColorBrush(bgColor),
                BorderBrush = new SolidColorBrush(C("#454545")),
                BorderThickness = new Thickness(0, 0, isLastCol ? 0 : 1, isLastRow ? 0 : 1)
            };
        }

        private static List<string> ParseCells(string line)
        {
            var parts = line.Split('|');
            var cells = new List<string>();
            for (int i = 1; i < parts.Length - 1; i++)
                cells.Add(parts[i].Trim());
            return cells;
        }

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
