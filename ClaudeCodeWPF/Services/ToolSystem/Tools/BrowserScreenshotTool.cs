using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Browser;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BrowserScreenshotTool : IToolExecutor
    {
        public string Name        => "browser_screenshot";
        public string Description => "對當前瀏覽器頁面截圖，儲存為 PNG 並回傳檔案路徑。";
        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""save_path"": { ""type"": ""string"", ""description"": ""儲存路徑（選填）"" }
            }
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var svc = CdpBrowserService.Instance;
            if (!svc.IsConnected) return ToolResult.Failure("尚未連接 CDP，請先執行 browser_connect");
            try
            {
                var savePath = input["save_path"]?.ToString();
                var path = await svc.CaptureScreenshotAsync(savePath);
                await svc.UpdatePageInfoAsync();
                return ToolResult.Success($"✅ 截圖已儲存: {path}\n頁面: {svc.CurrentTitle}");
            }
            catch (Exception ex) { return ToolResult.Failure($"截圖失敗: {ex.Message}"); }
        }
    }
}
