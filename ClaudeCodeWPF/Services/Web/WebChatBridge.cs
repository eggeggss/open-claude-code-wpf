using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services.Web
{
    public class WebChatBridge
    {
        private static WebChatBridge _instance;
        public static WebChatBridge Instance => _instance ?? (_instance = new WebChatBridge());

        private readonly object _lock = new object();
        private readonly Dictionary<string, CancellationTokenSource> _running =
            new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);

        private WebChatBridge()
        {
        }

        public event Action<string> OnSessionUpdated;

        public bool IsRunning(string sessionId)
        {
            lock (_lock)
            {
                return _running.ContainsKey(sessionId);
            }
        }

        public JObject GetState(string sessionId)
        {
            var session = ConversationManager.Instance.FindSession(sessionId);
            if (session == null)
                throw new InvalidOperationException("找不到指定的對話 session。");

            var messages = new JArray();
            var snapshot = session.Messages.ToArray();
            foreach (var msg in snapshot)
            {
                messages.Add(new JObject
                {
                    ["role"] = msg.Role ?? "",
                    ["content"] = msg.Content ?? "",
                    ["name"] = msg.Name ?? "",
                    ["tool_call_id"] = msg.ToolCallId ?? ""
                });
            }

            return new JObject
            {
                ["sessionId"] = session.Id,
                ["title"] = session.Title ?? "新對話",
                ["provider"] = session.Provider ?? ConfigService.Instance.CurrentProvider,
                ["model"] = session.Model ?? ConfigService.Instance.CurrentModel,
                ["isRunning"] = IsRunning(sessionId),
                ["messages"] = messages
            };
        }

        public Task StartSendAsync(string sessionId, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("訊息不可為空。", nameof(message));

            CancellationTokenSource cts;
            lock (_lock)
            {
                if (_running.ContainsKey(sessionId))
                    throw new InvalidOperationException("此 session 已有回應正在產生中。");

                cts = new CancellationTokenSource();
                _running[sessionId] = cts;
            }

            Task.Run(async () =>
            {
                try
                {
                    await SendMessageCoreAsync(sessionId, message, cts.Token);
                }
                finally
                {
                    lock (_lock)
                    {
                        if (_running.TryGetValue(sessionId, out var current) && current == cts)
                            _running.Remove(sessionId);
                    }
                    cts.Dispose();
                    OnSessionUpdated?.Invoke(sessionId);
                }
            });

            return Task.CompletedTask;
        }

        public bool Cancel(string sessionId)
        {
            lock (_lock)
            {
                if (!_running.TryGetValue(sessionId, out var cts))
                    return false;

                cts.Cancel();
                return true;
            }
        }

        private async Task SendMessageCoreAsync(string sessionId, string message, CancellationToken cancellationToken)
        {
            var session = ConversationManager.Instance.FindSession(sessionId);
            if (session == null)
                throw new InvalidOperationException("找不到指定的對話 session。");

            await BroadcastAsync(sessionId, "user_message", new JObject
            {
                ["content"] = message
            });

            var chatService = new ChatService();
            chatService.OnEvent += evt => BroadcastStreamEvent(sessionId, evt);
            chatService.OnToolStarted += (name, id, input) => BroadcastFireAndForget(sessionId, "tool_started", new JObject
            {
                ["name"] = name ?? "",
                ["id"] = id ?? "",
                ["input"] = input ?? ""
            });
            chatService.OnToolCompleted += (name, id, result) => BroadcastFireAndForget(sessionId, "tool_completed", new JObject
            {
                ["name"] = name ?? "",
                ["id"] = id ?? "",
                ["result"] = result ?? ""
            });
            chatService.OnToolFailed += (name, id, error) => BroadcastFireAndForget(sessionId, "tool_failed", new JObject
            {
                ["name"] = name ?? "",
                ["id"] = id ?? "",
                ["error"] = error ?? ""
            });

            try
            {
                await chatService.SendMessageAsync(session, message, cancellationToken);
                OnSessionUpdated?.Invoke(sessionId);
            }
            catch (OperationCanceledException)
            {
                await BroadcastAsync(sessionId, "cancelled", new JObject());
            }
            catch (Exception ex)
            {
                await BroadcastAsync(sessionId, "error", new JObject
                {
                    ["message"] = ex.Message
                });
            }
        }

        private void BroadcastStreamEvent(string sessionId, StreamEvent evt)
        {
            var data = new JObject
            {
                ["type"] = evt.Type.ToString()
            };

            if (!string.IsNullOrEmpty(evt.TextDelta))
                data["text"] = evt.TextDelta;
            if (!string.IsNullOrEmpty(evt.Error))
                data["message"] = evt.Error;
            if (!string.IsNullOrEmpty(evt.StopReason))
                data["stopReason"] = evt.StopReason;
            data["isFinalTurn"] = evt.IsFinalTurn;

            if (evt.ToolCall != null)
            {
                data["toolCall"] = new JObject
                {
                    ["id"] = evt.ToolCall.Id ?? "",
                    ["name"] = evt.ToolCall.Name ?? "",
                    ["arguments"] = evt.ToolCall.Arguments ?? new JObject()
                };
            }

            if (evt.Usage != null)
            {
                data["usage"] = new JObject
                {
                    ["inputTokens"] = evt.Usage.InputTokens,
                    ["outputTokens"] = evt.Usage.OutputTokens
                };
            }

            BroadcastFireAndForget(sessionId, ToWebEventName(evt.Type), data);
        }

        private static string ToWebEventName(StreamEventType type)
        {
            switch (type)
            {
                case StreamEventType.MessageStart: return "message_start";
                case StreamEventType.TextDelta: return "text_delta";
                case StreamEventType.ThinkingDelta: return "thinking_delta";
                case StreamEventType.ToolCallStart: return "tool_call_start";
                case StreamEventType.ToolCallDelta: return "tool_call_delta";
                case StreamEventType.ToolCallComplete: return "tool_call_complete";
                case StreamEventType.ToolResultsReady: return "tool_results_ready";
                case StreamEventType.MessageEnd: return "message_end";
                case StreamEventType.Error: return "error";
                case StreamEventType.Usage: return "usage";
                case StreamEventType.ContextWarning: return "context_warning";
                default: return "stream_event";
            }
        }

        private static Task BroadcastAsync(string sessionId, string eventName, JObject data)
        {
            return WebSseHub.Instance.BroadcastAsync(sessionId, eventName, data);
        }

        private static void BroadcastFireAndForget(string sessionId, string eventName, JObject data)
        {
            Task.Run(() => WebSseHub.Instance.BroadcastAsync(sessionId, eventName, data));
        }
    }
}
