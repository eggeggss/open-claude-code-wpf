using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserTypeTool : IToolExecutor
    {
        public string Name        => "browser_type";
        public string Description => "向目前焦點元素輸入鍵盤文字，或按特殊鍵（Enter/Tab/Escape/ArrowDown/ArrowUp）。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""text"": { ""type"": ""string"", ""description"": ""要輸入的文字"" },
                ""key"":  { ""type"": ""string"", ""description"": ""特殊鍵: Enter/Tab/Escape/ArrowDown/ArrowUp"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try
            {
                if (input["key"] != null)
                    return ToolResult.Success(await svc.PressKeyAsync(input["key"].ToString()));
                var text = input["text"]?.ToString() ?? "";
                return ToolResult.Success(await svc.TypeTextAsync(text));
            }
            catch (Exception ex) { return ToolResult.Failure($"輸入失敗: {ex.Message}"); }
        }
    }
}
