using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Desktop
{
    /// <summary>
    /// Desktop automation using native Windows UIAutomation.
    /// No external server required — works with Win32, WPF, WinForms, and UWP apps.
    /// </summary>
    public class DesktopService
    {
        private static DesktopService _instance;
        public static DesktopService Instance => _instance ?? (_instance = new DesktopService());
        private DesktopService() { }

        private AutomationElement _currentWindow;
        private Process _currentProcess;

        public bool HasWindow => _currentWindow != null;

        // ── Win32 P/Invoke (mouse click fallback) ───────────────────────────
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, IntPtr extra);
        private const uint MOUSEEVENTF_LEFTDOWN  = 0x0002, MOUSEEVENTF_LEFTUP  = 0x0004,
                           MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;

        // ── Launch / Attach ─────────────────────────────────────────────────
        public AutomationElement LaunchApp(string appPath, string arguments = "")
        {
            _currentProcess?.Dispose();
            _currentProcess = Process.Start(new ProcessStartInfo(appPath)
            {
                UseShellExecute = true,
                Arguments = arguments ?? ""
            });

            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(500);
                _currentProcess.Refresh();
                if (_currentProcess.MainWindowHandle != IntPtr.Zero) break;
            }

            if (_currentProcess.MainWindowHandle != IntPtr.Zero)
                _currentWindow = AutomationElement.FromHandle(_currentProcess.MainWindowHandle);

            return _currentWindow;
        }

        public AutomationElement AttachToWindow(string title)
        {
            _currentWindow = FindWindowByTitle(title);
            if (_currentWindow == null)
                throw new Exception($"找不到標題含有 '{title}' 的視窗");
            return _currentWindow;
        }

        public List<string> GetTopLevelWindowNames()
        {
            var root = AutomationElement.RootElement;
            var cond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
            return root.FindAll(TreeScope.Children, cond)
                .Cast<AutomationElement>()
                .Select(e => e.Current.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
        }

        private AutomationElement FindWindowByTitle(string title)
        {
            var root = AutomationElement.RootElement;
            var all  = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            var exact = all.Cast<AutomationElement>().FirstOrDefault(e =>
                string.Equals(e.Current.Name, title, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            return all.Cast<AutomationElement>().FirstOrDefault(e =>
                e.Current.Name?.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── Find Elements ───────────────────────────────────────────────────
        public AutomationElement GetContext() => _currentWindow ?? AutomationElement.RootElement;

        public AutomationElement FindElement(string strategy, string value)
            => GetContext().FindFirst(TreeScope.Descendants, BuildCondition(strategy, value));

        public List<AutomationElement> FindElements(string strategy, string value)
            => GetContext().FindAll(TreeScope.Descendants, BuildCondition(strategy, value))
                           .Cast<AutomationElement>().ToList();

        public Condition BuildCondition(string strategy, string value)
        {
            switch ((strategy ?? "name").ToLower().Trim())
            {
                case "automation_id":
                case "accessibility_id":
                case "id":
                    return new PropertyCondition(AutomationElement.AutomationIdProperty, value, PropertyConditionFlags.IgnoreCase);
                case "class_name":
                case "class":
                    return new PropertyCondition(AutomationElement.ClassNameProperty, value, PropertyConditionFlags.IgnoreCase);
                case "control_type":
                case "type":
                    return BuildControlTypeCond(value);
                default: // "name"
                    return new PropertyCondition(AutomationElement.NameProperty, value, PropertyConditionFlags.IgnoreCase);
            }
        }

        private Condition BuildControlTypeCond(string t)
        {
            switch (t.ToLower())
            {
                case "button":   return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                case "edit":     case "textbox":  case "input":
                    return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit);
                case "text":     case "label":
                    return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text);
                case "checkbox": return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox);
                case "combobox": return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox);
                case "list":     return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List);
                case "listitem": return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem);
                case "menu":     return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu);
                case "menuitem": return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem);
                case "tab":      return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab);
                case "tabitem":  return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                case "window":   return new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
                default:         return new PropertyCondition(AutomationElement.NameProperty, t, PropertyConditionFlags.IgnoreCase);
            }
        }

        // ── Click ───────────────────────────────────────────────────────────
        public void ClickElement(AutomationElement el, string clickType = "left")
        {
            if (clickType == "left")
            {
                try
                {
                    var inv = el.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                    if (inv != null) { inv.Invoke(); return; }
                }
                catch { }
                try
                {
                    var sel = el.GetCurrentPattern(SelectionItemPattern.Pattern) as SelectionItemPattern;
                    if (sel != null) { sel.Select(); return; }
                }
                catch { }
            }

            // Win32 mouse click fallback
            var rect = el.Current.BoundingRectangle;
            int cx = (int)(rect.Left + rect.Width  / 2);
            int cy = (int)(rect.Top  + rect.Height / 2);
            SetCursorPos(cx, cy);
            Thread.Sleep(50);
            if (clickType == "right")
            {
                mouse_event(MOUSEEVENTF_RIGHTDOWN, cx, cy, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_RIGHTUP,   cx, cy, 0, IntPtr.Zero);
            }
            else if (clickType == "double")
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, cx, cy, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP,   cx, cy, 0, IntPtr.Zero);
                Thread.Sleep(50);
                mouse_event(MOUSEEVENTF_LEFTDOWN, cx, cy, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP,   cx, cy, 0, IntPtr.Zero);
            }
            else
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, cx, cy, 0, IntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP,   cx, cy, 0, IntPtr.Zero);
            }
        }

        // ── Type ────────────────────────────────────────────────────────────
        public void TypeIntoElement(AutomationElement el, string text, bool clearFirst)
        {
            try
            {
                var vp = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                if (vp != null)
                {
                    vp.SetValue(clearFirst ? text : vp.Current.Value + text);
                    return;
                }
            }
            catch { }

            el.SetFocus();
            Thread.Sleep(100);
            if (clearFirst) System.Windows.Forms.SendKeys.SendWait("^a{DEL}");
            System.Windows.Forms.SendKeys.SendWait(EscapeSendKeys(text));
        }

        // ── Get Text ────────────────────────────────────────────────────────
        public string GetElementText(AutomationElement el)
        {
            try
            {
                var tp = el.GetCurrentPattern(TextPattern.Pattern) as TextPattern;
                if (tp != null) return tp.DocumentRange.GetText(-1);
            }
            catch { }
            try
            {
                var vp = el.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                if (vp != null) return vp.Current.Value;
            }
            catch { }
            return el.Current.Name;
        }

        // ── Key Press ───────────────────────────────────────────────────────
        public void SendKeysToContext(string sendKeysStr)
        {
            _currentWindow?.SetFocus();
            Thread.Sleep(100);
            System.Windows.Forms.SendKeys.SendWait(sendKeysStr);
        }

        private string EscapeSendKeys(string text)
            => text.Replace("{", "{{}")
                   .Replace("}", "{}}")
                   .Replace("(", "{(}").Replace(")", "{)}")
                   .Replace("[", "{[}").Replace("]", "{]}")
                   .Replace("+", "{+}").Replace("^", "{^}")
                   .Replace("%", "{%}").Replace("~", "{~}");

        // ── Screenshot ──────────────────────────────────────────────────────
        public string TakeScreenshot(string savePath = null, AutomationElement element = null)
        {
            if (string.IsNullOrEmpty(savePath))
                savePath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"desktop_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            System.Drawing.Rectangle rect;
            if (element != null)
            {
                var br = element.Current.BoundingRectangle;
                rect = new System.Drawing.Rectangle(
                    (int)br.Left, (int)br.Top, (int)br.Width, (int)br.Height);
            }
            else if (_currentWindow != null)
            {
                var br = _currentWindow.Current.BoundingRectangle;
                rect = new System.Drawing.Rectangle(
                    (int)br.Left, (int)br.Top, (int)br.Width, (int)br.Height);
            }
            else
            {
                var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                rect = screen;
            }

            using (var bmp = new System.Drawing.Bitmap(rect.Width, rect.Height))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.Location, System.Drawing.Point.Empty, rect.Size);
                bmp.Save(savePath, System.Drawing.Imaging.ImageFormat.Png);
            }
            return savePath;
        }

        // ── Close ───────────────────────────────────────────────────────────
        public void CloseApp()
        {
            try
            {
                var wp = _currentWindow?.GetCurrentPattern(WindowPattern.Pattern) as WindowPattern;
                wp?.Close();
            }
            catch { }
            try { if (_currentProcess != null && !_currentProcess.HasExited) _currentProcess.Kill(); } catch { }
            _currentProcess = null;
            _currentWindow = null;
        }
    }
}
