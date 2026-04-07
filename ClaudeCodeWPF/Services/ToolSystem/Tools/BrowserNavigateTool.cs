using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserNavigateTool : IToolExecutor
    {
        public string Name        => "browser_navigate";
        public string Description => "導航至指定 URL，等待頁面載入後回傳標題與 URL。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""required"": [""url""],
            ""properties"": {
                ""url"": { ""type"": ""string"", ""description"": ""目標 URL"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var url = input["url"]?.ToString();
            if (string.IsNullOrEmpty(url)) return ToolResult.Failure("必須提供 url");
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try { return ToolResult.Success(await svc.NavigateAsync(url)); }
            catch (Exception ex) { return ToolResult.Failure($"導航失敗: {ex.Message}"); }
        }
    }
}
