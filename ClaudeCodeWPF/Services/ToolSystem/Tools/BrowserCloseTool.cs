using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserCloseTool : IToolExecutor
    {
        public string Name        => "browser_close";
        public string Description => "關閉 CDP 連線（不會關閉瀏覽器本身）。";
        public JObject InputSchema => JObject.Parse(@"{""type"": ""object"", ""properties"": {}}");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            try
            {
                await CdpBrowserService.Instance.DisconnectAsync();
                return ToolResult.Success("✅ CDP 連線已關閉");
            }
            catch (Exception ex) { return ToolResult.Failure($"關閉失敗: {ex.Message}"); }
        }
    }
}
