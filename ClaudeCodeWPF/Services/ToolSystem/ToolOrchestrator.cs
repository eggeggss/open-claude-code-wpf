using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services.ToolSystem
{
    /// <summary>
    /// 工具編排器 — 接收模型的 tool_call 請求，路由到正確工具，收集結果
    /// </summary>
    public class ToolOrchestrator
    {
        private readonly ToolRegistry _registry;

        public ToolOrchestrator(ToolRegistry registry = null)
        {
            _registry = registry ?? ToolRegistry.Instance;
        }

        public event Action<string, string, string> OnToolStarted;   // toolName, toolId, input
        public event Action<string, string, string> OnToolCompleted; // toolName, toolId, result
        public event Action<string, string, string> OnToolFailed;    // toolName, toolId, error

        /// <summary>
        /// 執行一批 tool calls，回傳 tool result messages
        /// </summary>
        public async Task<List<ChatMessage>> ExecuteToolCallsAsync(
            List<ToolCall> toolCalls,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var results = new List<ChatMessage>();

            foreach (var tc in toolCalls)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var inputStr = tc.Arguments?.ToString() ?? "{}";
                OnToolStarted?.Invoke(tc.Name, tc.Id, inputStr);

                try
                {
                    if (!_registry.TryGetTool(tc.Name, out var tool))
                    {
                        var errResult = ToolResult.Failure($"Unknown tool: {tc.Name}");
                        OnToolFailed?.Invoke(tc.Name, tc.Id, errResult.Error);
                        results.Add(ChatMessage.ToolResponse(tc.Id, errResult.Content, tc.Name));
                        continue;
                    }

                    var result = await tool.ExecuteAsync(tc.Arguments, cancellationToken);
                    var content = result.IsSuccess ? result.Content : $"Error: {result.Error}";

                    OnToolCompleted?.Invoke(tc.Name, tc.Id, content);
                    var resultMsg = ChatMessage.ToolResponse(tc.Id, content, tc.Name);
                    if (result.Images != null && result.Images.Count > 0)
                        resultMsg.ImageBlocks = result.Images;
                    results.Add(resultMsg);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    OnToolFailed?.Invoke(tc.Name, tc.Id, ex.Message);
                    results.Add(ChatMessage.ToolResponse(tc.Id, $"Error: {ex.Message}", tc.Name));
                }
            }

            return results;
        }
    }
}
