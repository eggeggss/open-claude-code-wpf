using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// AI 摘要壓縮服務 — 移植自 claude-code 的 compactConversation。
    /// 呼叫目前 Provider 對整段對話歷史生成詳細摘要，再清除舊訊息並以摘要取代。
    /// </summary>
    public static class CompactService
    {
        // 移植自 claude-code/src/services/compact/prompt.ts: BASE_COMPACT_PROMPT
        private const string BaseCompactSystemPrompt =
            "You are a helpful AI assistant tasked with summarizing conversations.";

        private const string BaseCompactUserPrompt =
@"Your task is to create a detailed summary of the conversation so far, paying close attention to the user's explicit requests and your previous actions.
This summary should be thorough in capturing technical details, code patterns, and architectural decisions that would be essential for continuing development work without losing context.

Your summary should include the following sections:

1. Primary Request and Intent: Capture all of the user's explicit requests and intents in detail.
2. Key Technical Concepts: List all important technical concepts, technologies, and frameworks discussed.
3. Files and Code Sections: Enumerate specific files and code sections examined, modified, or created. Include code snippets where applicable.
4. Errors and Fixes: List all errors encountered and how they were fixed. Include any specific user feedback.
5. Problem Solving: Document problems solved and any ongoing troubleshooting efforts.
6. All User Messages: List ALL user messages (non-tool-results). These are critical for understanding intent.
7. Pending Tasks: Outline any pending tasks explicitly requested.
8. Current Work: Describe in detail what was being worked on immediately before this summary request.
9. Optional Next Step: List the next step directly in line with the most recent user request.

Respond with the summary only, no preamble.";

        /// <summary>
        /// 使用目前 Provider 生成對話摘要。
        /// </summary>
        /// <param name="messages">要摘要的訊息列表。</param>
        /// <param name="customInstructions">使用者自訂的摘要指示（可為 null）。</param>
        /// <param name="ct">取消 token。</param>
        /// <returns>生成的摘要文字；失敗時回傳 null。</returns>
        public static async Task<string> SummarizeAsync(
            List<ChatMessage> messages,
            string customInstructions = null,
            CancellationToken ct = default(CancellationToken))
        {
            if (messages == null || messages.Count == 0)
                return null;

            var provider = ModelProviderFactory.Instance.GetCurrentProvider();
            if (provider == null || !provider.IsAvailable)
                return null;

            // 建立摘要請求訊息（不包含圖片，避免超出 token 限制）
            var summaryMessages = BuildSummaryMessages(messages, customInstructions);

            var parameters = new ModelParameters
            {
                Model    = ConfigService.Instance.CurrentModel,
                MaxTokens   = 8192,
                Temperature = 0.0,
            };

            try
            {
                var response = await provider.SendMessageAsync(
                    summaryMessages,
                    BaseCompactSystemPrompt,
                    parameters,
                    tools: null,
                    cancellationToken: ct);

                return response?.Content;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CompactService] 摘要生成失敗: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 將摘要注入回訊息列表：清除所有舊訊息，插入一對「用戶問 → 助理答（摘要）」的訊息。
        /// 移植自 claude-code compact.ts: replaceOldMessagesWithSummary。
        /// </summary>
        public static void ApplySummary(List<ChatMessage> messages, string summary)
        {
            if (messages == null || string.IsNullOrWhiteSpace(summary)) return;

            messages.Clear();

            // 插入邊界訊息對，使後續對話能看到先前的歷史摘要
            messages.Add(ChatMessage.User(
                "[Compact Summary]\n以下是先前對話的摘要，由 /compact 指令自動生成。\n請在後續回應時參考此摘要作為上下文。"));

            messages.Add(new ChatMessage
            {
                Role    = "assistant",
                Content = summary,
            });
        }

        // ────────────────────────────────────────────────────────────────
        // Private helpers
        // ────────────────────────────────────────────────────────────────

        private static List<ChatMessage> BuildSummaryMessages(
            List<ChatMessage> messages,
            string customInstructions)
        {
            // 將對話歷史序列化為純文字（剝除圖片，避免超出 token 限制）
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<conversation_history>");

            foreach (var m in messages)
            {
                string role = m.Role == "assistant" ? "Assistant" : "User";
                string content = m.Content ?? "";

                // 截斷超長工具回應（保留前 2000 字）
                if (content.Length > 2000 && m.Role == "tool")
                    content = content.Substring(0, 2000) + "\n[...truncated...]";

                sb.AppendLine($"[{role}]: {content}");
            }

            sb.AppendLine("</conversation_history>");

            // 加上自訂指示
            string userPrompt = BaseCompactUserPrompt;
            if (!string.IsNullOrWhiteSpace(customInstructions))
                userPrompt += $"\n\nAdditional summarization instructions: {customInstructions}";

            userPrompt = sb.ToString() + "\n" + userPrompt;

            return new List<ChatMessage> { ChatMessage.User(userPrompt) };
        }
    }
}
