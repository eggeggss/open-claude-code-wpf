using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 所有模型供應商的統一介面
    /// </summary>
    public interface IModelProvider
    {
        string ProviderName { get; }

        /// <summary>非串流對話</summary>
        Task<ModelResponse> SendMessageAsync(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            List<ToolDefinition> tools = null,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>串流對話（使用回調推送事件）</summary>
        Task SendMessageStreamAsync(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            System.Action<StreamEvent> onEvent,
            List<ToolDefinition> tools = null,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>取得可用模型列表</summary>
        Task<List<ModelInfo>> GetAvailableModelsAsync();

        /// <summary>是否可用（API Key 已設定等）</summary>
        bool IsAvailable { get; }
    }

    public class ModelResponse
    {
        public string Content { get; set; }
        public List<ToolCall> ToolCalls { get; set; }
        public string StopReason { get; set; }
        public TokenUsage Usage { get; set; }

        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }
}
