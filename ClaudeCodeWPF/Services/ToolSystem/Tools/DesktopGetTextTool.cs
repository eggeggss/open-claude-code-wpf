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
    public class DesktopGetTextTool : IToolExecutor
    {
        public string Name => "desktop_get_text";
        public string Description =>
            "Get text from a UI element or read all visible text in the current window. " +
            "Omit strategy/value to read the entire window.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""strategy"": { ""type"": ""string"",  ""description"": ""name / automation_id / control_type / class_name (optional)"" },
                ""value"":    { ""type"": ""string"",  ""description"": ""Locator value (optional)"" },
                ""all_text"": { ""type"": ""boolean"", ""description"": ""Collect all text elements in current window (default: false)"" }
            },
            ""required"": []
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var strategy = input["strategy"]?.ToString();
            var value    = input["value"]?.ToString();
            bool allText = input["all_text"]?.Value<bool>() == true;

            try
            {
                var svc = DesktopService.Instance;

                if (allText || (string.IsNullOrWhiteSpace(strategy) && string.IsNullOrWhiteSpace(value)))
                {
                    // Collect all text from current context
                    var ctx = svc.GetContext();
                    var texts = CollectAllText(ctx);
                    return Task.FromResult(ToolResult.Success(
                        $"視窗: {ctx.Current.Name}\n\n{string.Join("\n", texts)}"));
                }

                var el = svc.FindElement(strategy, value);
                if (el == null)
                    return Task.FromResult(ToolResult.Failure($"找不到元素 ({strategy}='{value}')"));

                var text = svc.GetElementText(el);
                return Task.FromResult(ToolResult.Success(
                    $"[{el.Current.Name}] {text}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"取得文字失敗: {ex.Message}"));
            }
        }

        private List<string> CollectAllText(AutomationElement root)
        {
            var results = new List<string>();
            try
            {
                var all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                foreach (AutomationElement el in all)
                {
                    try
                    {
                        var name = el.Current.Name?.Trim();
                        if (!string.IsNullOrEmpty(name) && name.Length > 0)
                            results.Add($"{el.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")}: {name}");
                    }
                    catch { }
                }
            }
            catch { }
            return results;
        }
    }
}
