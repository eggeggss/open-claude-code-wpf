using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 使用當前 AI 模型為對話自動生成簡短標題。
    /// 在第一輪對話完成後呼叫，取代截斷式標題。
    /// </summary>
    public class TitleGeneratorService
    {
        private static TitleGeneratorService _instance;
        public static TitleGeneratorService Instance => _instance ?? (_instance = new TitleGeneratorService());

        private TitleGeneratorService() { }

        /// <summary>
        /// 根據對話的第一組 user/assistant 訊息，呼叫 AI 生成標題。
        /// 失敗時回傳 null，呼叫端應 fallback 至截斷式標題。
        /// </summary>
        public async Task<string> GenerateTitleAsync(string userMessage, string assistantMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage)) return null;

            try
            {
                var provider = ModelProviderFactory.Instance.GetCurrentProvider();
                if (provider == null) return null;

                // Truncate inputs so the title-gen call is cheap
                var userSnippet      = Truncate(userMessage,      300);
                var assistantSnippet = Truncate(assistantMessage, 300);

                // IMPORTANT: last message MUST be "user" — if it ends with "assistant",
                // the API will try to continue that message instead of generating a new reply.
                // Embed the whole conversation inside a single user turn asking for a title.
                var prompt = string.IsNullOrWhiteSpace(assistantSnippet)
                    ? $"請根據以下訊息生成一個簡短標題（繁體中文，15字以內，只回答標題）：\n\n{userSnippet}"
                    : $"請根據以下對話生成一個簡短標題（繁體中文，15字以內，只回答標題）：\n\n用戶：{userSnippet}\n\n助理：{assistantSnippet}";

                var messages = new List<ChatMessage>
                {
                    new ChatMessage { Role = "user", Content = prompt }
                };

                var response = await provider.SendMessageAsync(
                    messages,
                    null,   // no system prompt — keep it simple & universal
                    new ModelParameters
                    {
                        Model       = ConfigService.Instance.CurrentModel,
                        MaxTokens   = 64,
                        Temperature = 0.3
                    });

                var title = response?.Content?.Trim();
                if (string.IsNullOrWhiteSpace(title)) return null;

                // Strip surrounding quotes/brackets the model might add
                title = Regex.Replace(title, @"^[\s「『""'\[【《〈]+|[\s」』""'\]】》〉]+$", "").Trim();

                // Discard if model hallucinated a long paragraph
                if (title.Length > 60) return null;

                return title;
            }
            catch
            {
                return null;
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
        }
    }
}
