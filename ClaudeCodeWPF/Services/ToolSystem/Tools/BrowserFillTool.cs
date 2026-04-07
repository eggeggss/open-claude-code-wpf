using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserFillTool : IToolExecutor
    {
        public string Name        => "browser_fill";
        public string Description => "在 input/textarea 填入文字（CSS selector），自動觸發 React/Vue/Angular input 事件。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""required"": [""selector"", ""value""],
            ""properties"": {
                ""selector"": { ""type"": ""string"", ""description"": ""CSS 選擇器"" },
                ""value"":    { ""type"": ""string"", ""description"": ""要填入的值"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var sel = input["selector"]?.ToString();
            var val = input["value"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(sel)) return ToolResult.Failure("必須提供 selector");
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try { return ToolResult.Success("✅ " + await svc.FillAsync(sel, val)); }
            catch (Exception ex) { return ToolResult.Failure($"填入失敗: {ex.Message}"); }
        }
    }
}
