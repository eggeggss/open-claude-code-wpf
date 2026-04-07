using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 對話與工具呼叫日誌服務
    /// 每天一個檔案，格式為 JSON Lines，儲存在 exe 所在目錄的 logs\ 子資料夾
    /// 使用方式：LogService.Instance.LogXxx(...)
    /// </summary>
    public class LogService
    {
        private static LogService _instance;
        public static LogService Instance => _instance ?? (_instance = new LogService());

        private readonly string _logDir;
        private readonly object _lock = new object();

        // ── 限制單一工具結果寫入長度，避免大型輸出撐爆日誌 ─────────────────
        private const int MaxResultLength = 2000;

        private LogService()
        {
            // 放在 exe 同一層的 logs\ 資料夾，方便直接查看
            _logDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "logs");
            Directory.CreateDirectory(_logDir);
        }

        // ── 公開 API ──────────────────────────────────────────────────────────

        public void LogUserMessage(string sessionId, string content)
        {
            Write(new JObject
            {
                ["type"]    = "user",
                ["session"] = sessionId,
                ["content"] = Truncate(content, 4000)
            });
        }

        public void LogAssistantMessage(string sessionId, string content)
        {
            Write(new JObject
            {
                ["type"]    = "assistant",
                ["session"] = sessionId,
                ["content"] = Truncate(content, 4000)
            });
        }

        public void LogToolStart(string sessionId, string toolName, string toolId, string inputJson)
        {
            Write(new JObject
            {
                ["type"]    = "tool_start",
                ["session"] = sessionId,
                ["tool"]    = toolName,
                ["id"]      = toolId,
                ["input"]   = TryParseJson(inputJson) ?? (JToken)inputJson
            });
        }

        public void LogToolDone(string sessionId, string toolName, string toolId, string result, long elapsedMs)
        {
            Write(new JObject
            {
                ["type"]    = "tool_done",
                ["session"] = sessionId,
                ["tool"]    = toolName,
                ["id"]      = toolId,
                ["result"]  = Truncate(result, MaxResultLength),
                ["ms"]      = elapsedMs
            });
        }

        public void LogToolError(string sessionId, string toolName, string toolId, string error, long elapsedMs)
        {
            Write(new JObject
            {
                ["type"]    = "tool_error",
                ["session"] = sessionId,
                ["tool"]    = toolName,
                ["id"]      = toolId,
                ["error"]   = error,
                ["ms"]      = elapsedMs
            });
        }

        public void LogError(string context, string error)
        {
            Write(new JObject
            {
                ["type"]    = "error",
                ["context"] = context,
                ["error"]   = error
            });
        }

        /// <summary>回傳今天日誌檔案路徑（供 UI 開啟資料夾用）</summary>
        public string TodayLogPath => Path.Combine(_logDir, $"{DateTime.Now:yyyy-MM-dd}.jsonl");

        /// <summary>回傳日誌資料夾路徑</summary>
        public string LogDirectory => _logDir;

        // ── 內部寫入 ──────────────────────────────────────────────────────────

        private void Write(JObject entry)
        {
            try
            {
                entry["time"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff");

                var line = entry.ToString(Formatting.None);
                var path = TodayLogPath;

                lock (_lock)
                {
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日誌失敗不應影響主程式
            }
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + $"…[truncated {s.Length - max} chars]";
        }

        private static JToken TryParseJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return JToken.Parse(s); }
            catch { return null; }
        }
    }
}
