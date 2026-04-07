using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Desktop;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class DesktopScreenshotTool : IToolExecutor
    {
        public string Name => "desktop_screenshot";
        public string Description =>
            "Take a screenshot of the current window (or full screen if no window attached). " +
            "Saves as PNG and returns the file path.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""save_path"": { ""type"": ""string"", ""description"": ""Optional path to save screenshot"" }
            },
            ""required"": []
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var savePath = input["save_path"]?.ToString();
            try
            {
                var path = DesktopService.Instance.TakeScreenshot(savePath);
                return Task.FromResult(ToolResult.Success($"截圖已儲存至: {path}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"截圖失敗: {ex.Message}"));
            }
        }
    }
}
