using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.MCP
{
    /// <summary>將 MCP 工具橋接到本地 IToolExecutor 介面</summary>
    public class MCPToolBridge : IToolExecutor
    {
        private readonly MCPTool _mcpTool;
        private readonly MCPClient _client;

        public MCPToolBridge(MCPTool mcpTool, MCPClient client)
        {
            _mcpTool = mcpTool;
            _client = client;
        }

        public string Name => $"mcp__{_client.Config.Name}__{_mcpTool.Name}";
        public string Description => $"[MCP:{_client.Config.Name}] {_mcpTool.Description}";
        public JObject InputSchema => _mcpTool.InputSchema ?? new JObject { ["type"] = "object", ["properties"] = new JObject() };

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (!_client.IsConnected)
                return ToolResult.Failure($"MCP server '{_client.Config.Name}' is not connected");

            try
            {
                var result = await _client.CallToolAsync(_mcpTool.Name, input, cancellationToken);

                // Extract content from MCP tool result
                var content = result?["content"];
                if (content is JArray arr && arr.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in arr)
                    {
                        if (item["type"]?.ToString() == "text")
                            sb.AppendLine(item["text"]?.ToString());
                    }
                    return ToolResult.Success(sb.ToString().TrimEnd());
                }

                return ToolResult.Success(result?.ToString() ?? "");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
            }
        }
    }
}
