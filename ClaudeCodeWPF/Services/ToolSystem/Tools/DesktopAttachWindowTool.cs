using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Desktop;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class DesktopAttachWindowTool : IToolExecutor
    {
        public string Name => "desktop_attach_window";
        public string Description =>
            "Attach to an already-running application window by its title. " +
            "Set list_windows: true to see all available window titles first.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""window_title"": { ""type"": ""string"",  ""description"": ""Partial or full window title to attach to"" },
                ""list_windows"": { ""type"": ""boolean"", ""description"": ""If true, list all top-level windows (default: false)"" }
            },
            ""required"": []
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            bool listMode = input["list_windows"]?.Value<bool>() == true;

            try
            {
                if (listMode)
                {
                    var names = DesktopService.Instance.GetTopLevelWindowNames();
                    return Task.FromResult(ToolResult.Success(
                        $"目前開啟的視窗 ({names.Count}):\n" + string.Join("\n", names)));
                }

                var title = input["window_title"]?.ToString();
                if (string.IsNullOrWhiteSpace(title))
                    return Task.FromResult(ToolResult.Failure("請提供 window_title，或設定 list_windows: true"));

                var el = DesktopService.Instance.AttachToWindow(title);
                return Task.FromResult(ToolResult.Success($"已附加至視窗: {el.Current.Name}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"附加視窗失敗: {ex.Message}"));
            }
        }
    }
}
