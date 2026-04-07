using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Desktop;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class DesktopTypeTool : IToolExecutor
    {
        public string Name => "desktop_type";
        public string Description =>
            "Type text into a UI element (text box, search field, etc.). " +
            "Uses ValuePattern for WPF/WinForms inputs, falls back to SendKeys.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""strategy"":    { ""type"": ""string"",  ""description"": ""name / automation_id / control_type / class_name"" },
                ""value"":       { ""type"": ""string"",  ""description"": ""Locator value"" },
                ""text"":        { ""type"": ""string"",  ""description"": ""Text to type"" },
                ""clear_first"": { ""type"": ""boolean"", ""description"": ""Clear field before typing (default: true)"" }
            },
            ""required"": [""strategy"", ""value"", ""text""]
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var strategy   = input["strategy"]?.ToString() ?? "name";
            var value      = input["value"]?.ToString();
            var text       = input["text"]?.ToString() ?? "";
            bool clearFirst = input["clear_first"] == null || input["clear_first"].Value<bool>();

            if (string.IsNullOrWhiteSpace(value))
                return Task.FromResult(ToolResult.Failure("value is required"));

            try
            {
                var svc = DesktopService.Instance;
                var el  = svc.FindElement(strategy, value);
                if (el == null)
                    return Task.FromResult(ToolResult.Failure($"找不到元素 ({strategy}='{value}')"));

                svc.TypeIntoElement(el, text, clearFirst);
                return Task.FromResult(ToolResult.Success(
                    $"已在 '{el.Current.Name}' 輸入: {text}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"輸入失敗: {ex.Message}"));
            }
        }
    }
}
