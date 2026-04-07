using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Desktop;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class DesktopClickTool : IToolExecutor
    {
        public string Name => "desktop_click";
        public string Description =>
            "Click a UI element in the current desktop window. " +
            "Tries InvokePattern first, falls back to Win32 mouse click.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""strategy"":   { ""type"": ""string"", ""description"": ""name / automation_id / control_type / class_name"" },
                ""value"":      { ""type"": ""string"", ""description"": ""Locator value"" },
                ""click_type"": { ""type"": ""string"", ""description"": ""left (default) / right / double"" }
            },
            ""required"": [""strategy"", ""value""]
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var strategy  = input["strategy"]?.ToString() ?? "name";
            var value     = input["value"]?.ToString();
            var clickType = input["click_type"]?.ToString() ?? "left";

            if (string.IsNullOrWhiteSpace(value))
                return Task.FromResult(ToolResult.Failure("value is required"));

            try
            {
                var svc = DesktopService.Instance;
                var el  = svc.FindElement(strategy, value);
                if (el == null)
                    return Task.FromResult(ToolResult.Failure($"找不到元素 ({strategy}='{value}')"));

                svc.ClickElement(el, clickType);
                return Task.FromResult(ToolResult.Success(
                    $"已點擊 ({clickType}): {el.Current.Name}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"點擊失敗: {ex.Message}"));
            }
        }
    }
}
