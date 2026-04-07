using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserClickTool : IToolExecutor
    {
        public string Name        => "browser_click";
        public string Description => "點擊頁面元素（CSS selector）。例如: #btnSubmit, .btn-primary, button[type=submit]";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""required"": [""selector""],
            ""properties"": {
                ""selector"": { ""type"": ""string"", ""description"": ""CSS 選擇器"" },
                ""wait_ms"":  { ""type"": ""integer"", ""description"": ""點擊後等待毫秒，預設 500"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var sel = input["selector"]?.ToString();
            if (string.IsNullOrEmpty(sel)) return ToolResult.Failure("必須提供 selector");
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try
            {
                var result = await svc.ClickAsync(sel);
                var wait   = input["wait_ms"]?.Value<int>() ?? 500;
                await Task.Delay(wait, ct);
                return ToolResult.Success($"✅ 已點擊: {result}");
            }
            catch (Exception ex) { return ToolResult.Failure($"點擊失敗: {ex.Message}"); }
        }
    }
}
