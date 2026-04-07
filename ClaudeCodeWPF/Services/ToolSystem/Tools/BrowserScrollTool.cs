using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserScrollTool : IToolExecutor
    {
        public string Name        => "browser_scroll";
        public string Description => "滾動頁面。direction: down/up/left/right，或用 x/y 指定像素。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""direction"": { ""type"": ""string"", ""enum"": [""down"",""up"",""left"",""right""] },
                ""amount"":    { ""type"": ""integer"", ""description"": ""滾動像素，預設 300"" },
                ""x"":         { ""type"": ""integer"" },
                ""y"":         { ""type"": ""integer"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try
            {
                int dx = input["x"]?.Value<int>() ?? 0;
                int dy = input["y"]?.Value<int>() ?? 0;
                if (dx == 0 && dy == 0)
                {
                    var dir = input["direction"]?.ToString() ?? "down";
                    var amt = input["amount"]?.Value<int>() ?? 300;
                    if      (dir == "down")  dy =  amt;
                    else if (dir == "up")    dy = -amt;
                    else if (dir == "right") dx =  amt;
                    else if (dir == "left")  dx = -amt;
                }
                return ToolResult.Success(await svc.ScrollAsync(dx, dy));
            }
            catch (Exception ex) { return ToolResult.Failure($"滾動失敗: {ex.Message}"); }
        }
    }
}
