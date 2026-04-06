using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Models
{
    public class ChatMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; } // "user", "assistant", "system", "tool"

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<ToolCall> ToolCalls { get; set; }

        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolCallId { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonIgnore]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public TokenUsage Usage { get; set; }

        [JsonIgnore]
        public List<ImageBlock> ImageBlocks { get; set; }

        public static ChatMessage User(string content)
        {
            return new ChatMessage { Role = "user", Content = content };
        }

        public static ChatMessage Assistant(string content, List<ToolCall> toolCalls = null)
        {
            return new ChatMessage { Role = "assistant", Content = content, ToolCalls = toolCalls };
        }

        public static ChatMessage System(string content)
        {
            return new ChatMessage { Role = "system", Content = content };
        }

        public static ChatMessage ToolResponse(string toolCallId, string content, string name = null)
        {
            return new ChatMessage { Role = "tool", ToolCallId = toolCallId, Content = content, Name = name };
        }

        public static ChatMessage UserWithImages(string text, List<ImageBlock> images)
        {
            return new ChatMessage { Role = "user", Content = text, ImageBlocks = images };
        }
    }

    public class ToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = "function";

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public JObject Arguments { get; set; }

        /// <summary>Used during streaming to accumulate raw JSON arguments delta</summary>
        [JsonIgnore]
        public string RawArgsDelta { get; set; }

        /// <summary>Arguments delta text for streaming events</summary>
        [JsonIgnore]
        public string ArgumentsDelta { get; set; }
    }

    public class TokenUsage
    {
        [JsonProperty("input_tokens")]
        public int InputTokens { get; set; }

        [JsonProperty("output_tokens")]
        public int OutputTokens { get; set; }

        [JsonProperty("cache_read_input_tokens")]
        public int CacheReadInputTokens { get; set; }

        [JsonProperty("cache_creation_input_tokens")]
        public int CacheCreationInputTokens { get; set; }

        public int TotalTokens => InputTokens + OutputTokens;
    }
}
