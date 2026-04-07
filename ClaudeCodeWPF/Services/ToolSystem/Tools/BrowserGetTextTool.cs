using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserGetTextTool : IToolExecutor
    {
        public string Name        => "browser_get_text";
        public string Description => "取得當前頁面的文字或簡化 DOM 結構（含 id/class/value，用於找到正確 CSS selector）。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""mode"": {
                    ""type"": ""string"",
                    ""enum"": [""text"", ""dom""],
                    ""description"": ""text=純文字(預設), dom=簡化DOM結構(含selector資訊)""
                }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try
            {
                await svc.UpdatePageInfoAsync();
                var header = $"頁面: {svc.CurrentTitle}\nURL: {svc.CurrentUrl}\n\n";
                var mode   = input["mode"]?.ToString() ?? "text";
                if (mode == "dom")
                    return ToolResult.Success(header + await svc.GetSimplifiedDomAsync());
                var text = await svc.GetPageTextAsync();
                if (text.Length > 5000) text = text.Substring(0, 5000) + "\n...(已截斷)";
                return ToolResult.Success(header + text);
            }
            catch (Exception ex) { return ToolResult.Failure($"取得頁面失敗: {ex.Message}"); }
        }
    }
}
