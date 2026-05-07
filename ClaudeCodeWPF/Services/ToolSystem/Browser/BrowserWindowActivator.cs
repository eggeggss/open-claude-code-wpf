using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Browser
{
    internal static class BrowserWindowActivator
    {
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        public static bool TryBringToFront(string preferredTitle)
        {
            var handles = FindBrowserWindows(preferredTitle);
            foreach (var handle in handles)
            {
                if (TryActivate(handle))
                    return true;
            }
            return false;
        }

        private static IEnumerable<IntPtr> FindBrowserWindows(string preferredTitle)
        {
            var matched = new List<IntPtr>();
            var fallback = new List<IntPtr>();
            var processNames = new[] { "msedge", "chrome" };

            foreach (var processName in processNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        var handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                            continue;

                        var title = process.MainWindowTitle ?? "";
                        if (!string.IsNullOrWhiteSpace(preferredTitle) &&
                            title.IndexOf(preferredTitle, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matched.Add(handle);
                        }
                        else
                        {
                            fallback.Add(handle);
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            matched.AddRange(fallback);
            return matched;
        }

        private static bool TryActivate(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return false;

            ShowWindowAsync(hWnd, IsIconic(hWnd) ? SW_RESTORE : SW_SHOW);

            var currentThreadId = GetCurrentThreadId();
            var foregroundWindow = GetForegroundWindow();
            var foregroundThreadId = foregroundWindow == IntPtr.Zero
                ? 0
                : GetWindowThreadProcessId(foregroundWindow, out _);
            var targetThreadId = GetWindowThreadProcessId(hWnd, out _);

            var attachedForeground = false;
            var attachedTarget = false;

            try
            {
                if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
                    attachedForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
                if (targetThreadId != 0 && targetThreadId != currentThreadId)
                    attachedTarget = AttachThreadInput(currentThreadId, targetThreadId, true);

                BringWindowToTop(hWnd);
                return SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attachedTarget)
                    AttachThreadInput(currentThreadId, targetThreadId, false);
                if (attachedForeground)
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
    }
}
