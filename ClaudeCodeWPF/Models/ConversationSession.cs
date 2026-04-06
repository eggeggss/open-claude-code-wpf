using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OpenClaudeCodeWPF.Models
{
    public class ConversationSession
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("title")]
        public string Title { get; set; } = "新對話";

        [JsonProperty("messages")]
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("system_prompt")]
        public string SystemPrompt { get; set; }

        [JsonIgnore]
        public ModelParameters Parameters { get; set; } = new ModelParameters();

        [JsonIgnore]
        public int TotalInputTokens { get; set; }

        [JsonIgnore]
        public int TotalOutputTokens { get; set; }

        [JsonIgnore]
        public double EstimatedCost { get; set; }

        public void UpdateTitle()
        {
            if (Messages.Count > 0 && Title == "新對話")
            {
                var firstMsg = Messages[0].Content;
                Title = firstMsg.Length > 50 ? firstMsg.Substring(0, 50) + "..." : firstMsg;
            }
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public class ModelParameters
    {
        public string Model { get; set; }
        public int MaxTokens { get; set; } = 8192;
        public double Temperature { get; set; } = 0.7;
        public int ThinkingBudget { get; set; } = 0;
        public bool Streaming { get; set; } = true;
    }
}
