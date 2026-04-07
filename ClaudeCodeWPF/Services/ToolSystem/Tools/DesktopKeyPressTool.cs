using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Desktop;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class DesktopKeyPressTool : IToolExecutor
    {
        public string Name => "desktop_key_press";
        public string Description =>
            "Press a keyboard shortcut or key in the current window. " +
            "Examples: 'Control+c', 'Alt+F4', 'Control+Shift+s', 'Enter', 'Escape', 'F5'.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""keys"": {
                    ""type"": ""string"",
                    ""description"": ""Key combination. Examples: 'Control+c', 'Alt+F4', 'Enter', 'F5'""
                }
            },
            ""required"": [""keys""]
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default)
        {
            var keys = input["keys"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(keys))
                return Task.FromResult(ToolResult.Failure("keys is required"));

            try
            {
                var sendKeysStr = ConvertToSendKeys(keys);
                DesktopService.Instance.SendKeysToContext(sendKeysStr);
                return Task.FromResult(ToolResult.Success($"已按下: {keys} (SendKeys: {sendKeysStr})"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolResult.Failure($"按鍵失敗: {ex.Message}"));
            }
        }

        private string ConvertToSendKeys(string keys)
        {
            var parts = keys.Split('+');
            var result = "";
            var modifiers = "";

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].ToLower().Trim())
                {
                    case "control": case "ctrl": modifiers += "^"; break;
                    case "shift":                modifiers += "+"; break;
                    case "alt":                  modifiers += "%"; break;
                    case "win": case "windows":  modifiers += "^%"; break;
                }
            }

            var mainKey = parts[parts.Length - 1].Trim();
            var resolved = ResolveKey(mainKey);

            if (!string.IsNullOrEmpty(modifiers) && resolved.StartsWith("{"))
                result = modifiers + resolved;
            else if (!string.IsNullOrEmpty(modifiers))
                result = $"{modifiers}{resolved}";
            else
                result = resolved;

            return result;
        }

        private string ResolveKey(string key)
        {
            switch (key.ToLower())
            {
                case "enter": case "return": return "{ENTER}";
                case "escape": case "esc":   return "{ESC}";
                case "tab":                  return "{TAB}";
                case "backspace":            return "{BACKSPACE}";
                case "delete": case "del":   return "{DELETE}";
                case "home":                 return "{HOME}";
                case "end":                  return "{END}";
                case "pageup":               return "{PGUP}";
                case "pagedown":             return "{PGDN}";
                case "up":                   return "{UP}";
                case "down":                 return "{DOWN}";
                case "left":                 return "{LEFT}";
                case "right":                return "{RIGHT}";
                case "f1":  return "{F1}";  case "f2":  return "{F2}";
                case "f3":  return "{F3}";  case "f4":  return "{F4}";
                case "f5":  return "{F5}";  case "f6":  return "{F6}";
                case "f7":  return "{F7}";  case "f8":  return "{F8}";
                case "f9":  return "{F9}";  case "f10": return "{F10}";
                case "f11": return "{F11}"; case "f12": return "{F12}";
                case "a": return "a"; case "c": return "c"; case "v": return "v";
                case "x": return "x"; case "z": return "z"; case "s": return "s";
                default: return key.Length == 1 ? key : $"{{{key.ToUpper()}}}";
            }
        }
    }
}
