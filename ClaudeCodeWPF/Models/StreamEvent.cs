using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Models
{
    public enum StreamEventType
    {
        TextDelta,
        ToolCallStart,
        ToolCallArgsDelta,
        ToolCallDelta,
        ToolCallComplete,
        ToolResultsReady,
        ToolResult,
        ThinkingDelta,
        MessageStart,
        MessageEnd,
        FullMessage,
        Error,
        Usage,
        ContextWarning,    // Emitted before API call when context usage is high
    }

    public class StreamEvent
    {
        public StreamEventType Type { get; set; }

        /// <summary>Text content (TextDelta, ThinkingDelta)</summary>
        public string TextDelta { get; set; }

        /// <summary>Legacy alias for TextDelta</summary>
        public string Text { get => TextDelta; set => TextDelta = value; }

        public ToolCall ToolCall { get; set; }
        public ToolResult ToolResult { get; set; }
        public TokenUsage Usage { get; set; }
        public string StopReason { get; set; }
        public string Error { get; set; }

        /// <summary>For ContextWarning: current context usage % (0-100+)</summary>
        public double ContextPercent { get; set; }

        /// <summary>For ContextWarning: number of old messages trimmed before this call</summary>
        public int TrimmedCount { get; set; }

        public static StreamEvent MessageStartEvent()
        {
            return new StreamEvent { Type = StreamEventType.MessageStart };
        }

        public static StreamEvent TextDeltaEvent(string text)
        {
            return new StreamEvent { Type = StreamEventType.TextDelta, TextDelta = text };
        }

        public static StreamEvent ThinkingDeltaEvent(string text)
        {
            return new StreamEvent { Type = StreamEventType.ThinkingDelta, TextDelta = text };
        }

        public static StreamEvent ErrorEvent(string error)
        {
            return new StreamEvent { Type = StreamEventType.Error, Error = error };
        }

        public static StreamEvent UsageEvent(TokenUsage usage)
        {
            return new StreamEvent { Type = StreamEventType.Usage, Usage = usage };
        }

        public static StreamEvent MessageEndEvent(string stopReason, TokenUsage usage = null)
        {
            return new StreamEvent { Type = StreamEventType.MessageEnd, StopReason = stopReason, Usage = usage };
        }

        public static StreamEvent ContextWarningEvent(double percent, int trimmed = 0)
        {
            return new StreamEvent
            {
                Type = StreamEventType.ContextWarning,
                ContextPercent = percent,
                TrimmedCount = trimmed,
            };
        }
    }
}
