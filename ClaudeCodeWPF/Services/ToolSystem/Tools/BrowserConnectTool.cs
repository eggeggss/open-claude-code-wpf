using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserConnectTool : IToolExecutor
    {
        public string Name        => "browser_connect";
        public string Description => "連接至以 --remote-debugging-port=9222 啟動的 Chrome/Edge。" +
                                     "必須先呼叫此工具才能使用其他 browser_ 工具。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""port"":       { ""type"": ""integer"", ""description"": ""CDP 通訊埠，預設 9222"" },
                ""tab_url"":    { ""type"": ""string"",  ""description"": ""優先連接含此 URL 的分頁"" },
                ""wait_ready"": { ""type"": ""boolean"", ""description"": ""剛啟動 Edge/Chrome 時設為 true，會自動等待 CDP 就緒（最多 20 秒）"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var port      = input["port"]?.Value<int>() ?? 9222;
            var tabUrl    = input["tab_url"]?.ToString();
            var waitReady = input["wait_ready"]?.Value<bool>() ?? true; // 預設等待，減少時序失敗
            try
            {
                var svc      = CdpBrowserService.Instance;
                var maxWait  = waitReady ? 20000 : 3000;
                var tabs     = await svc.GetTabsAsync(port, maxWait);
                if (tabs.Count == 0)
                    return ToolResult.Failure(
                        $"找不到瀏覽器分頁。請先以 --remote-debugging-port={port} 啟動 Chrome/Edge：\n" +
                        "Edge:   msedge.exe --remote-debugging-port=9222\n" +
                        "Chrome: chrome.exe --remote-debugging-port=9222");
                await svc.ConnectAsync(port: port, tabUrl: tabUrl);
                var list = "";
                for (var i = 0; i < Math.Min(tabs.Count, 5); i++)
                    list += $"  [{i}] {tabs[i].Title} — {tabs[i].Url}\n";
                return ToolResult.Success(
                    $"✅ 已連接 CDP (port {port})\n目前分頁: {svc.CurrentTitle}\nURL: {svc.CurrentUrl}\n\n可用分頁:\n{list}");
            }
            catch (Exception ex)
            {
                return ToolResult.Failure($"連接失敗: {ex.Message}\n請確認已用 --remote-debugging-port={port} 啟動 Chrome/Edge");
            }
        }
    }
}
