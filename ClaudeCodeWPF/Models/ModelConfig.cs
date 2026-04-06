using Newtonsoft.Json;

namespace OpenClaudeCodeWPF.Models
{
    public class ModelConfig
    {
        public string ProviderName { get; set; }
        public string ModelId { get; set; }
        public string DisplayName { get; set; }
        public string ApiKey { get; set; }
        public string BaseUrl { get; set; }
        public bool SupportsStreaming { get; set; } = true;
        public bool SupportsFunctionCalling { get; set; } = true;
        public bool SupportsVision { get; set; } = false;
        public int MaxContextTokens { get; set; } = 128000;
        public int MaxOutputTokens { get; set; } = 8192;
    }

    public class ModelInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string DisplayName { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonProperty("supports_tools")]
        public bool SupportsTools { get; set; } = true;

        [JsonProperty("supports_streaming")]
        public bool SupportsStreaming { get; set; } = true;

        [JsonProperty("supports_vision")]
        public bool SupportsVision { get; set; } = false;

        [JsonProperty("supports_thinking")]
        public bool SupportsThinking { get; set; } = false;

        /// <summary>Human-readable capability summary for ComboBox ToolTip.</summary>
        [JsonIgnore]
        public string CapabilityTip
        {
            get
            {
                var caps = new System.Collections.Generic.List<string>();
                if (SupportsTools)     caps.Add("工具呼叫");
                if (SupportsVision)    caps.Add("多模態/視覺");
                if (SupportsThinking)  caps.Add("延伸思考");
                if (SupportsStreaming) caps.Add("串流");
                var capStr = caps.Count > 0 ? string.Join(" · ", caps) : "基本文字";
                return $"{Id}\nMax tokens: {MaxTokens:N0}\n能力: {capStr}";
            }
        }

        public override string ToString() => $"{Provider}: {DisplayName} ({Id})";
    }
}
