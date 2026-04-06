using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    /// <summary>
    /// Sub-agent tool — launches a new ChatService instance with a focused task
    /// </summary>
    public class AgentTool : IToolExecutor
    {
        public string Name => "Agent";
        public string Description => "Launch a sub-agent to perform a focused task. The sub-agent has access to all tools and will return its final answer.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""task"": { ""type"": ""string"", ""description"": ""The task description for the sub-agent"" },
                ""context"": { ""type"": ""string"", ""description"": ""Additional context to provide to the sub-agent"" }
            },
            ""required"": [""task""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var task = input["task"]?.ToString();
            if (string.IsNullOrEmpty(task)) return ToolResult.Failure("task is required");

            var context = input["context"]?.ToString() ?? "";
            var fullPrompt = string.IsNullOrEmpty(context) ? task : $"{context}\n\n{task}";

            try
            {
                // Create a sub-agent using ChatService
                var subAgent = new ChatService();
                var result = await subAgent.RunSingleAsync(fullPrompt, cancellationToken);
                return ToolResult.Success(result);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure($"Sub-agent failed: {ex.Message}");
            }
        }
    }
}
