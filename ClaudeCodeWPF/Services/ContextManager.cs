using System;
using System.Collections.Generic;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 上下文視窗管理 — 估算 token 用量、警告門檻、自動截斷舊訊息
    /// 原版 claude-code 有完整的 autoCompact/microCompact 系統，
    /// 此處實作簡化版：估算 + 截斷最舊輪次，避免 prompt_too_long 錯誤。
    /// </summary>
    public static class ContextManager
    {
        // 1 token ≈ 3.5 chars (英文 ~4, 中文 ~2, 取平均保守估算)
        private const double CharsPerToken = 3.5;

        // 各 Provider 預設上下文視窗大小 (tokens)
        private static readonly System.Collections.Generic.Dictionary<string, int> ContextWindows =
            new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Anthropic",   200_000 },
            { "Gemini",    1_000_000 },
            { "OpenAI",      128_000 },
            { "AzureOpenAI", 128_000 },
            { "Ollama",        8_192 },
        };

        // 警告門檻：超過 80% 就顯示黃色警告
        public const double WarnThreshold  = 80.0;
        // 錯誤門檻：超過 90% 就顯示紅色
        public const double ErrorThreshold = 90.0;
        // 自動截斷目標：截斷後保持在 70% 以下
        public const double TrimTarget     = 70.0;

        /// <summary>依 provider 取得上下文視窗大小。</summary>
        public static int GetContextWindow(string provider)
        {
            if (!string.IsNullOrEmpty(provider) && ContextWindows.TryGetValue(provider, out int size))
                return size;
            return 128_000;
        }

        /// <summary>估算單一字串的 token 數量。</summary>
        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return Math.Max(1, (int)(text.Length / CharsPerToken));
        }

        /// <summary>估算訊息清單的總 token 數量（含 system prompt）。</summary>
        public static int EstimateTokens(List<ChatMessage> messages, string systemPrompt = null)
        {
            int total = 0;

            // system prompt
            if (!string.IsNullOrEmpty(systemPrompt))
                total += EstimateTokens(systemPrompt) + 4;

            // 訊息列表
            foreach (var m in messages)
            {
                total += 4; // per-message overhead
                total += EstimateTokens(m.Content);

                if (m.ToolCalls != null)
                    foreach (var tc in m.ToolCalls)
                    {
                        total += EstimateTokens(tc.Name);
                        total += EstimateTokens(tc.Arguments?.ToString());
                    }
            }
            return total;
        }

        /// <summary>計算目前上下文使用百分比 (0~100+)。</summary>
        public static double GetUsagePercent(List<ChatMessage> messages, string provider, string systemPrompt = null)
        {
            int contextWindow = GetContextWindow(provider);
            int estimated     = EstimateTokens(messages, systemPrompt);
            return estimated / (double)contextWindow * 100.0;
        }

        /// <summary>
        /// 強制截斷至指定百分比（不管目前是否超過門檻）。
        /// 用於 /compact 指令手動壓縮。回傳移除的訊息數。
        /// </summary>
        public static int TrimToTarget(List<ChatMessage> messages, string provider,
            string systemPrompt = null, double targetPercent = 70.0)
        {
            int contextWindow = GetContextWindow(provider);
            int targetTokens  = (int)(contextWindow * targetPercent / 100.0);
            int removed = 0;

            while (messages.Count > 2)
            {
                int current = EstimateTokens(messages, systemPrompt);
                if (current <= targetTokens) break;

                int removeEnd = FindEndOfOldestTurn(messages);
                for (int i = removeEnd; i >= 0; i--)
                {
                    messages.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>
        /// 檢查並截斷：若超過 TrimTarget 就從最舊的輪次開始移除。
        /// 保留至少 2 則訊息（最後 1 輪使用者 + 助理）。
        /// 回傳截斷前的使用率和截斷後的結果。
        /// </summary>
        public static ContextStatus CheckAndTrim(
            List<ChatMessage> messages,
            string provider,
            string systemPrompt = null)
        {
            int contextWindow = GetContextWindow(provider);
            int estimated     = EstimateTokens(messages, systemPrompt);
            double usagePct   = estimated / (double)contextWindow * 100.0;

            int trimmedCount = 0;

            // 若超過截斷目標，移除最舊的輪次
            if (usagePct > TrimTarget && messages.Count > 2)
            {
                int targetTokens = (int)(contextWindow * TrimTarget / 100.0);

                while (messages.Count > 2)
                {
                    int current = EstimateTokens(messages, systemPrompt);
                    if (current <= targetTokens) break;

                    // 找到最舊一輪的結束邊界（user → assistant + 任何 tool results）
                    int removeEnd = FindEndOfOldestTurn(messages);
                    for (int i = removeEnd; i >= 0; i--)
                    {
                        messages.RemoveAt(i);
                        trimmedCount++;
                    }
                }

                estimated = EstimateTokens(messages, systemPrompt);
                usagePct  = estimated / (double)contextWindow * 100.0;
            }

            return new ContextStatus
            {
                EstimatedTokens = estimated,
                ContextWindow   = contextWindow,
                UsagePercent    = usagePct,
                TrimmedCount    = trimmedCount,
                IsWarning       = usagePct >= WarnThreshold,
                IsError         = usagePct >= ErrorThreshold,
            };
        }

        /// <summary>
        /// 找到最舊一輪的結束 index（含 user, assistant, tool results）。
        /// 從 index 0 掃到下一個 user 訊息為止。
        /// </summary>
        private static int FindEndOfOldestTurn(List<ChatMessage> messages)
        {
            if (messages.Count == 0) return -1;

            // 從 index 1 開始找下一個 user 訊息（代表新一輪開始）
            for (int i = 1; i < messages.Count - 1; i++)
            {
                if (messages[i].Role == "user")
                    return i - 1; // 刪除 0 ~ i-1
            }

            // 找不到下一輪，只刪第一則
            return 0;
        }
    }

    /// <summary>上下文狀態快照。</summary>
    public class ContextStatus
    {
        public int    EstimatedTokens { get; set; }
        public int    ContextWindow   { get; set; }
        public double UsagePercent    { get; set; }
        public int    TrimmedCount    { get; set; }
        public bool   IsWarning       { get; set; }
        public bool   IsError         { get; set; }

        public string GetDisplayText()
        {
            string pct = $"Ctx {UsagePercent:F0}%";
            if (TrimmedCount > 0)
                pct += $" (已截斷{TrimmedCount}則)";
            return pct;
        }
    }
}
