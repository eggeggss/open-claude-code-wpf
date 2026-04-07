using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Desktop;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class DesktopFindElementTool : IToolExecutor
    {
        public string Name => "desktop_find_element";
        public string Description =>
            "Find UI elements in the current window using UIAutomation. " +
            "Strategies: 'name' (label/title), 'automation_id' (AutomationId/x:Name), " +
            "'control_type' (button/edit/text/checkbox/...), 'class_name'.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""strategy"": {
                    ""type"": ""string"",
                    ""enum"": [""name"", ""automation_id"", ""control_type"", ""class_name""],
                    ""description"": ""How to locate elements""
                },
                ""value"":    { ""type"": ""string"",  ""description"": ""Locator value"" },
                ""multiple"": { ""type"": ""boolean"", ""description"": ""Return all matches (default: false)"" }
            },
            ""required"": [""strategy"", ""value""]
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var strategy = input["strategy"]?.ToString() ?? "name";
            var value    = input["value"]?.ToString();
            bool multi   = input["multiple"]?.Value<bool>() == true;

            if (string.IsNullOrWhiteSpace(value))
                return Task.FromResult(ToolResult.Failure("value is required"));

            try
            {
                var svc = DesktopService.Instance;

                if (multi)
                {
                    var elements = svc.FindElements(strategy, value);
                    var lines = new List<string>();
                    for (int i = 0; i < elements.Count; i++)
                        lines.Add(FormatElement(i, elements[i]));
                    return Task.FromResult(ToolResult.Success(
                        $"找到 {elements.Count} 個元素:\n" + string.Join("\n", lines)));
                }
                else
                {
                    var el = svc.FindElement(strategy, value);
                    if (el == null)
                        return Task.FromResult(ToolResult.Failure($"找不到元素 ({strategy}='{value}')"));
                    return Task.FromResult(ToolResult.Success("找到元素:\n" + FormatElement(-1, el)));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"查找元素失敗: {ex.Message}"));
            }
        }

        private string FormatElement(int idx, AutomationElement el)
        {
            var cur  = el.Current;
            var text = "";
            try { text = DesktopService.Instance.GetElementText(el); } catch { }
            if (text?.Length > 60) text = text.Substring(0, 60) + "...";
            var prefix = idx >= 0 ? $"[{idx}] " : "";
            return $"{prefix}Name={cur.Name} | AutomationId={cur.AutomationId} | " +
                   $"Type={cur.ControlType.ProgrammaticName} | Text={text}";
        }
    }
}
