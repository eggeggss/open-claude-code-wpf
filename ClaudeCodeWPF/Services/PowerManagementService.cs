using System;
using System.Runtime.InteropServices;
using OpenClaudeCodeWPF.Services;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 使用 Win32 SetThreadExecutionState 防止螢幕保護程式、螢幕關閉與系統睡眠。
    /// 不影響瀏覽器前景功能（BrowserWindowActivator）。
    /// </summary>
    public class PowerManagementService
    {
        private static PowerManagementService _instance;
        public static PowerManagementService Instance => _instance ?? (_instance = new PowerManagementService());

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint ES_CONTINUOUS       = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED  = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        private bool _isEnabled;

        private PowerManagementService() { }

        /// <summary>根據 enabled 決定啟用或停用保持喚醒。</summary>
        public void Apply(bool enabled)
        {
            if (enabled)
                EnableKeepAwake();
            else
                DisableKeepAwake();
        }

        /// <summary>啟用保持喚醒：阻止螢幕保護程式、螢幕關閉與系統睡眠。</summary>
        public void EnableKeepAwake()
        {
            if (_isEnabled) return;
            uint result = SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            if (result == 0)
                LogService.Instance.LogError("PowerManagementService", "SetThreadExecutionState(enable) failed.");
            else
                _isEnabled = true;
        }

        /// <summary>停用保持喚醒，恢復 Windows 預設電源管理。</summary>
        public void DisableKeepAwake()
        {
            if (!_isEnabled) return;
            uint result = SetThreadExecutionState(ES_CONTINUOUS);
            if (result == 0)
                LogService.Instance.LogError("PowerManagementService", "SetThreadExecutionState(disable) failed.");
            else
                _isEnabled = false;
        }
    }
}
