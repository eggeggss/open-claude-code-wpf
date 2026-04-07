using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserSelectTool : IToolExecutor
    {
        public string Name        => "browser_select";
        public string Description => "選取 <select> 下拉選單選項（CSS selector + 選項值或顯示文字）。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""required"": [""selector"", ""value""],
            ""properties"": {
                ""selector"": { ""type"": ""string"", ""description"": ""CSS 選擇器 (select 元素)"" },
                ""value"":    { ""type"": ""string"", ""description"": ""選項 value 屬性或顯示文字"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var sel = input["selector"]?.ToString();
            var val = input["value"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(sel)) return ToolResult.Failure("必須提供 selector");
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try { return ToolResult.Success("✅ " + await svc.SelectAsync(sel, val)); }
            catch (Exception ex) { return ToolResult.Failure($"選取失敗: {ex.Message}"); }
        }
    }
}
