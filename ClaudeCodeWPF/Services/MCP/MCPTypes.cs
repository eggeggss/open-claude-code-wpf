using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.MCP
{
    // MCP Protocol Types (JSON-RPC 2.0 over stdio/SSE)

    public class MCPRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JObject Params { get; set; }
    }

    public class MCPResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; }

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("result")]
        public JObject Result { get; set; }

        [JsonProperty("error")]
        public MCPError Error { get; set; }

        public bool IsSuccess => Error == null;
    }

    public class MCPError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public JToken Data { get; set; }
    }

    public class MCPTool
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public JObject InputSchema { get; set; }
    }

    public class MCPServerConfig
    {
        public string Name { get; set; }
        public string Command { get; set; }
        public List<string> Args { get; set; } = new List<string>();
        public Dictionary<string, string> Env { get; set; } = new Dictionary<string, string>();
        public string Transport { get; set; } = "stdio"; // "stdio" or "sse"
        public string SseUrl { get; set; }
    }

    public class MCPResource
    {
        [JsonProperty("uri")]
        public string Uri { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }
    }

    public class MCPPrompt
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("arguments")]
        public List<MCPPromptArgument> Arguments { get; set; }
    }

    public class MCPPromptArgument
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("required")]
        public bool Required { get; set; }
    }

    public class MCPServerInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public List<MCPTool> Tools { get; set; } = new List<MCPTool>();
        public List<MCPResource> Resources { get; set; } = new List<MCPResource>();
        public List<MCPPrompt> Prompts { get; set; } = new List<MCPPrompt>();
    }
}
