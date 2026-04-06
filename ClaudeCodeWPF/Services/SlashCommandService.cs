using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 處理聊天輸入框的 /slash 指令。
    /// 移植自 claude-code 原版的 commands/ 目錄，取 WPF 適用子集。
    /// </summary>
    public class SlashCommandService
    {
        private static SlashCommandService _instance;
        public static SlashCommandService Instance => _instance ?? (_instance = new SlashCommandService());

        /// <summary>回呼：顯示系統訊息到聊天面板 (text, isError, fontSize, fontFamily)</summary>
        public Action<string, bool, double, string> ShowMessage;

        /// <summary>回呼：重新渲染目前 session（/clear 用）</summary>
        public Action RefreshChat;

        /// <summary>回呼：更新歷史面板（/rename 用）</summary>
        public Action RefreshHistory;

        // ── 便利包裝：一般訊息（使用預設字型）────────────────────────────
        private void Msg(string text, bool isError = false)
            => ShowMessage?.Invoke(text, isError, 12, "Consolas, Cascadia Code, Courier New");

        // ────────────────────────────────────────────────────────────────
        // 公開 API
        // ────────────────────────────────────────────────────────────────

        /// <summary>
        /// 判斷輸入是否為 slash 指令，若是則執行並回傳 true；
        /// 否則回傳 false（讓正常 AI 對話流程處理）。
        /// </summary>
        public bool TryHandle(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || !input.StartsWith("/"))
                return false;

            var parts = input.TrimStart('/').Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd  = parts.Length > 0 ? parts[0].ToLowerInvariant().Trim() : "";
            var args = parts.Length > 1 ? parts[1].Trim() : "";

            switch (cmd)
            {
                case "help":    HandleHelp();              return true;
                case "clear":   HandleClear();             return true;
                case "compact": HandleCompact();           return true;
                case "context": HandleContext();           return true;
                case "export":  HandleExport(args);        return true;
                case "rename":  HandleRename(args);        return true;
                case "cost":    HandleCost();              return true;
                default:
                    Msg($"❌ 未知指令：/{cmd}\n輸入 /help 查看可用指令清單。", true);
                    return true;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 指令實作
        // ────────────────────────────────────────────────────────────────

        private void HandleHelp()
        {
            var sb = new StringBuilder();
            sb.AppendLine("━━━ 可用指令 ━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("/help              顯示此說明");
            sb.AppendLine("/clear             清除對話歷史（保留 session）");
            sb.AppendLine("/compact           壓縮上下文：移除最舊輪次直到 70% 以下");
            sb.AppendLine("/context           顯示目前上下文視窗使用量");
            sb.AppendLine("/cost              顯示本次 session 的 token 用量統計");
            sb.AppendLine("/export            將對話匯出到剪貼簿（純文字）");
            sb.AppendLine("/export <路徑>     將對話匯出到指定檔案");
            sb.AppendLine("/rename <名稱>     重命名目前對話");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine("快捷鍵：Ctrl+N 新對話 │ Esc 取消生成");
            ShowMessage?.Invoke(sb.ToString(), false, 17, "Comic Sans MS");
        }

        private void HandleClear()
        {
            var session = ConversationManager.Instance.ActiveSession;
            if (session == null) { Msg("❌ 沒有作用中的對話。", true); return; }

            int count = session.Messages.Count;
            session.Messages.Clear();
            ConversationManager.Instance.SaveSession(session);
            RefreshChat?.Invoke();
            Msg($"✓ 已清除 {count} 則訊息。上下文已重置。", false);
        }

        private void HandleCompact()
        {
            var session = ConversationManager.Instance.ActiveSession;
            if (session == null || session.Messages.Count == 0)
            {
                Msg("ℹ 沒有可壓縮的訊息。", false);
                return;
            }

            var systemPrompt = SystemPromptService.Instance.GetSystemPrompt(
                ConfigService.Instance.Language);
            var provider = ConfigService.Instance.CurrentProvider;

            // 計算壓縮前
            double before = ContextManager.GetUsagePercent(session.Messages, provider, systemPrompt);
            int beforeCount = session.Messages.Count;

            // 執行截斷（目標 70%，即使目前低於 80% 也強制執行到 70%）
            int trimmed = ContextManager.TrimToTarget(session.Messages, provider, systemPrompt, 70.0);

            double after = ContextManager.GetUsagePercent(session.Messages, provider, systemPrompt);
            ConversationManager.Instance.SaveSession(session);

            if (trimmed == 0)
                Msg($"ℹ 上下文已在 {before:F0}%，無需壓縮。", false);
            else
            {
                Msg($"✓ 已壓縮上下文：{before:F0}% → {after:F0}%（移除 {trimmed} 則舊訊息）", false);
                RefreshChat?.Invoke();
            }
        }

        private void HandleContext()
        {
            var session = ConversationManager.Instance.ActiveSession;
            var messages = session?.Messages ?? new List<ChatMessage>();
            var systemPrompt = SystemPromptService.Instance.GetSystemPrompt(
                ConfigService.Instance.Language);
            var provider = ConfigService.Instance.CurrentProvider;

            int contextWindow  = ContextManager.GetContextWindow(provider);
            int estimated      = ContextManager.EstimateTokens(messages, systemPrompt);
            double pct         = estimated / (double)contextWindow * 100.0;

            string bar = BuildProgressBar(pct);
            string color = pct >= ContextManager.ErrorThreshold ? "⛔" :
                           pct >= ContextManager.WarnThreshold  ? "⚠" : "✅";

            var sb = new StringBuilder();
            sb.AppendLine("━━━ 上下文使用量 ━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"Provider : {provider}");
            sb.AppendLine($"估算 Tokens : {estimated:N0} / {contextWindow:N0}");
            sb.AppendLine($"使用率  : {color} {pct:F1}%");
            sb.AppendLine($"          {bar}");
            sb.AppendLine($"訊息數量 : {messages.Count} 則");
            if (pct > ContextManager.WarnThreshold)
                sb.AppendLine("→ 建議執行 /compact 壓縮上下文");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Msg(sb.ToString(), false);
        }

        private void HandleExport(string args)
        {
            var session = ConversationManager.Instance.ActiveSession;
            if (session == null || session.Messages.Count == 0)
            {
                Msg("❌ 沒有可匯出的對話。", true);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# {session.Title}");
            sb.AppendLine($"# 時間：{session.CreatedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"# Provider：{session.Provider} / {session.Model}");
            sb.AppendLine();

            foreach (var msg in session.Messages)
            {
                if (msg.Role == "system") continue;
                string role = msg.Role == "user" ? "使用者" : "助理";
                sb.AppendLine($"--- {role} ---");
                sb.AppendLine(msg.Content ?? "");
                if (msg.ToolCalls != null)
                    foreach (var tc in msg.ToolCalls)
                        sb.AppendLine($"[工具呼叫：{tc.Name}]");
                sb.AppendLine();
            }

            string text = sb.ToString();

            if (string.IsNullOrEmpty(args))
            {
                // 匯出到剪貼簿
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    Msg($"✓ 對話已複製到剪貼簿（{session.Messages.Count} 則訊息）", false);
                }
                catch (Exception ex)
                {
                    Msg($"❌ 複製失敗：{ex.Message}", true);
                }
            }
            else
            {
                // 匯出到檔案
                try
                {
                    string path = args.Trim('"', '\'');
                    if (!Path.IsPathRooted(path))
                        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), path);

                    File.WriteAllText(path, text, Encoding.UTF8);
                    Msg($"✓ 已匯出到：{path}", false);
                }
                catch (Exception ex)
                {
                    Msg($"❌ 匯出失敗：{ex.Message}", true);
                }
            }
        }

        private void HandleRename(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                Msg("用法：/rename <新名稱>", true);
                return;
            }

            var session = ConversationManager.Instance.ActiveSession;
            if (session == null) { Msg("❌ 沒有作用中的對話。", true); return; }

            session.Title = args.Trim();
            ConversationManager.Instance.SaveSession(session);
            RefreshHistory?.Invoke();
            Msg($"✓ 對話已重命名為：{session.Title}", false);
        }

        private void HandleCost()
        {
            var session = ConversationManager.Instance.ActiveSession;
            if (session == null) { Msg("ℹ 沒有作用中的對話。", false); return; }

            int totalIn  = session.TotalInputTokens;
            int totalOut = session.TotalOutputTokens;
            int msgs     = session.Messages.Count;

            // 粗估成本（以 Claude Sonnet 為基準：input $3/Mtok, output $15/Mtok）
            double costIn  = totalIn  / 1_000_000.0 * 3.0;
            double costOut = totalOut / 1_000_000.0 * 15.0;
            double total   = costIn + costOut;

            var sb = new StringBuilder();
            sb.AppendLine("━━━ Token 用量統計 ━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"輸入  tokens : {totalIn:N0}");
            sb.AppendLine($"輸出  tokens : {totalOut:N0}");
            sb.AppendLine($"訊息數量     : {msgs} 則");
            if (totalIn > 0 || totalOut > 0)
                sb.AppendLine($"估計費用     : ~${total:F4} USD（以 Sonnet 為基準）");
            else
                sb.AppendLine("（尚無 API 用量記錄，僅串流模式可統計）");
            sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Msg(sb.ToString(), false);
        }

        // ────────────────────────────────────────────────────────────────
        // 輔助
        // ────────────────────────────────────────────────────────────────

        private static string BuildProgressBar(double pct, int width = 20)
        {
            int filled = (int)(Math.Min(pct, 100.0) / 100.0 * width);
            return "[" + new string('█', filled) + new string('░', width - filled) + "]";
        }
    }
}
