using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Desktop;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class DesktopLaunchAppTool : IToolExecutor
    {
        public string Name => "desktop_launch_app";
        public string Description =>
            "Launch a Windows application. Uses native UIAutomation — no WinAppDriver needed. " +
            "Provide the full path to the .exe file (e.g. C:\\Windows\\System32\\notepad.exe).";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""app_path"":  { ""type"": ""string"", ""description"": ""Full path to the .exe file to launch"" },
                ""arguments"": { ""type"": ""string"", ""description"": ""Optional command-line arguments"" }
            },
            ""required"": [""app_path""]
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var appPath   = input["app_path"]?.ToString();
            var arguments = input["arguments"]?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(appPath))
                return Task.FromResult(ToolResult.Failure("app_path is required"));

            try
            {
                var el = DesktopService.Instance.LaunchApp(appPath, arguments);
                var title = el?.Current.Name ?? "(視窗未出現)";
                return Task.FromResult(ToolResult.Success(
                    $"已啟動: {appPath}\n視窗標題: {title}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"啟動失敗: {ex.Message}"));
            }
        }
    }
}
