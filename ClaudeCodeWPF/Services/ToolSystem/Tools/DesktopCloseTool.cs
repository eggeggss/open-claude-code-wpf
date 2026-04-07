using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Desktop;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class DesktopCloseTool : IToolExecutor
    {
        public string Name => "desktop_close";
        public string Description =>
            "Close the current desktop application being automated.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {},
            ""required"": []
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            try
            {
                DesktopService.Instance.CloseApp();
                return Task.FromResult(ToolResult.Success("已關閉應用程式"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"關閉失敗: {ex.Message}"));
            }
        }
    }
}
