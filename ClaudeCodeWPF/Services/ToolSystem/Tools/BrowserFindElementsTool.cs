using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserFindElementsTool : IToolExecutor
    {
        public string Name        => "browser_find_elements";
        public string Description => "用 CSS selector 搜尋頁面元素，回傳清單（含 id/name/type/value/text）。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""required"": [""selector""],
            ""properties"": {
                ""selector"": { ""type"": ""string"", ""description"": ""CSS 選擇器，例如 input, select, button, .form-control"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var sel = input["selector"]?.ToString();
            if (string.IsNullOrEmpty(sel)) return ToolResult.Failure("必須提供 selector");
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try { return ToolResult.Success(await svc.FindElementsAsync(sel)); }
            catch (Exception ex) { return ToolResult.Failure($"搜尋失敗: {ex.Message}"); }
        }
    }
}
