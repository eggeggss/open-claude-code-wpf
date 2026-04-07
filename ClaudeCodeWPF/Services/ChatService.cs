using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 核心對話服務 — 負責與模型互動的主迴圈
    /// 處理串流/非串流回應、工具呼叫、多輪對話
    /// </summary>
    public class ChatService
    {
        private readonly ToolRegistry _toolRegistry;
        private readonly ToolOrchestrator _orchestrator;
        private const int MaxToolIterations = 50;

        public event Action<StreamEvent> OnEvent;
        public event Action<string, string, string> OnToolStarted;
        public event Action<string, string, string> OnToolCompleted;
        public event Action<string, string, string> OnToolFailed;

        public ChatService()
        {
            _toolRegistry = ToolRegistry.Instance;
            _orchestrator = new ToolOrchestrator(_toolRegistry);

            _orchestrator.OnToolStarted += (name, id, input) => OnToolStarted?.Invoke(name, id, input);
            _orchestrator.OnToolCompleted += (name, id, result) => OnToolCompleted?.Invoke(name, id, result);
            _orchestrator.OnToolFailed += (name, id, error) => OnToolFailed?.Invoke(name, id, error);
        }

        /// <summary>
        /// 處理一輪用戶輸入 — 主入口點
        /// </summary>
        public async Task SendMessageAsync(
            ConversationSession session,
            string userInput,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            // Add user message to session
            var userMsg = ChatMessage.User(userInput);
            session.Messages.Add(userMsg);
            OnEvent?.Invoke(StreamEvent.MessageStartEvent());

            // Log user message
            LogService.Instance.LogUserMessage(session.Id, userInput);

            var provider = ModelProviderFactory.Instance.GetCurrentProvider();
            var systemPrompt = SystemPromptService.Instance.GetSystemPrompt(ConfigService.Instance.Language);
            var tools = _toolRegistry.GetAllToolDefinitions();
            var parameters = new ModelParameters
            {
                Model = ConfigService.Instance.CurrentModel,   // ← 傳入 UI 選擇的模型
                MaxTokens = ConfigService.Instance.MaxTokens,
                Temperature = ConfigService.Instance.Temperature,
                Streaming = ConfigService.Instance.StreamingEnabled
            };

            // Accumulates token usage across all rounds; forwarded with the final MessageEnd
            TokenUsage lastUsage = null;

            for (int iteration = 0; iteration < MaxToolIterations; iteration++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // ── Context window check ──────────────────────────────────────
                // Trim oldest turns if approaching limit; warn UI in any case.
                var ctxStatus = ContextManager.CheckAndTrim(
                    session.Messages,
                    ConfigService.Instance.CurrentProvider,
                    systemPrompt);

                if (ctxStatus.IsWarning || ctxStatus.TrimmedCount > 0)
                    OnEvent?.Invoke(StreamEvent.ContextWarningEvent(ctxStatus.UsagePercent, ctxStatus.TrimmedCount));
                // ─────────────────────────────────────────────────────────────

                List<ToolCall> toolCallsInResponse = null;
                var assistantContent = "";

                if (parameters.Streaming)
                {
                    // Streaming mode
                    // Intercept the provider's MessageEnd so we — not the provider — control
                    // when the UI re-enables input.  We re-emit it ourselves (IsFinalTurn correct).
                    var toolCallsAccum = new List<ToolCall>();
                    TokenUsage capturedUsage = null;
                    string capturedStopReason = "stop";

                    await provider.SendMessageStreamAsync(
                        session.Messages,
                        systemPrompt,
                        parameters,
                        onEvent: evt =>
                        {
                            // Suppress provider's MessageEnd — ChatService fires its own below
                            if (evt.Type == StreamEventType.MessageEnd)
                            {
                                if (evt.Usage != null) capturedUsage = evt.Usage;
                                capturedStopReason = evt.StopReason ?? capturedStopReason;
                                return;
                            }

                            OnEvent?.Invoke(evt);

                            if (evt.Type == StreamEventType.TextDelta)
                                assistantContent += evt.TextDelta ?? "";
                            else if (evt.Type == StreamEventType.ToolCallComplete && evt.ToolCall != null)
                            {
                                // Providers emit ToolCallComplete with fully built ToolCall (Arguments already set)
                                toolCallsAccum.Add(evt.ToolCall);
                            }
                        },
                        tools: tools,
                        cancellationToken: cancellationToken);

                    toolCallsInResponse = toolCallsAccum.Count > 0 ? toolCallsAccum : null;

                    // If more tool rounds follow, emit intermediate MessageEnd (IsFinalTurn=false)
                    // so the UI can close / finalise the current text bubble.
                    // The final MessageEnd (IsFinalTurn=true) is emitted after the loop below.
                    if (toolCallsInResponse != null)
                        OnEvent?.Invoke(StreamEvent.MessageEndEvent(capturedStopReason, capturedUsage, isFinalTurn: false));
                    else
                        // No tools — store usage so the final break can emit it
                        lastUsage = capturedUsage ?? lastUsage;
                }
                else
                {
                    // Non-streaming mode
                    var response = await provider.SendMessageAsync(
                        session.Messages,
                        systemPrompt,
                        parameters,
                        tools: tools,
                        cancellationToken: cancellationToken);

                    assistantContent = response.Content ?? "";
                    toolCallsInResponse = response.ToolCalls;
                    lastUsage = response.Usage ?? lastUsage;

                    // Emit text as single event for non-streaming
                    if (!string.IsNullOrEmpty(assistantContent))
                        OnEvent?.Invoke(StreamEvent.TextDeltaEvent(assistantContent));
                }

                // Add assistant message to session
                var assistantMsg = new ChatMessage
                {
                    Role = "assistant",
                    Content = assistantContent,
                    ToolCalls = toolCallsInResponse
                };
                session.Messages.Add(assistantMsg);

                // Log assistant response
                if (!string.IsNullOrEmpty(assistantContent))
                    LogService.Instance.LogAssistantMessage(session.Id, assistantContent);

                // If no tool calls, we're done — emit final MessageEnd (IsFinalTurn=true)
                if (toolCallsInResponse == null || toolCallsInResponse.Count == 0)
                {
                    OnEvent?.Invoke(StreamEvent.MessageEndEvent("end_turn", lastUsage, isFinalTurn: true));
                    break;
                }

                // Execute tool calls (pass sessionId for logging)
                var toolResults = await _orchestrator.ExecuteToolCallsAsync(toolCallsInResponse, cancellationToken, session.Id);

                // Add tool results to session messages
                foreach (var tr in toolResults)
                    session.Messages.Add(tr);

                // Signal tool results ready, loop continues
                OnEvent?.Invoke(new StreamEvent { Type = StreamEventType.ToolResultsReady });
            }

            // Auto-save after each turn
            ConversationManager.Instance.SaveSession(session);
            HistoryService.Instance.AddEntry(session);
        }

        /// <summary>
        /// 單次執行（用於 AgentTool 子代理）
        /// </summary>
        public async Task<string> RunSingleAsync(string prompt, CancellationToken cancellationToken = default(CancellationToken))
        {
            var session = new ConversationSession
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Sub-agent",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            var result = "";
            OnEvent += evt =>
            {
                if (evt.Type == StreamEventType.TextDelta)
                    result += evt.TextDelta ?? "";
            };

            await SendMessageAsync(session, prompt, cancellationToken);
            return result;
        }
    }
}
