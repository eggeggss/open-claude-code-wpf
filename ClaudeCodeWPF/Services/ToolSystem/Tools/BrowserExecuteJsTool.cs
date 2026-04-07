using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserExecuteJsTool : IToolExecutor
    {
        public string Name        => "browser_execute_js";
        public string Description => "在瀏覽器執行任意 JavaScript，回傳結果。適合複雜頁面互動或取得元素資訊。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""required"": [""script""],
            ""properties"": {
                ""script"":       { ""type"": ""string"",  ""description"": ""JavaScript 程式碼"" },
                ""await_promise"":{ ""type"": ""boolean"", ""description"": ""是否等待 Promise，預設 false"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var script = input["script"]?.ToString();
            if (string.IsNullOrEmpty(script)) return ToolResult.Failure("必須提供 script");
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try
            {
                var awaitPromise = input["await_promise"]?.Value<bool>() ?? false;
                var result = await svc.EvaluateAsync(script, awaitPromise);
                return ToolResult.Success(result != null ? result.ToString() : "(無回傳值)");
            }
            catch (Exception ex) { return ToolResult.Failure($"JS 執行失敗: {ex.Message}"); }
        }
    }
}
