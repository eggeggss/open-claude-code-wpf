using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem
{
    /// <summary>工具執行器統一介面</summary>
    public interface IToolExecutor
    {
        string Name { get; }
        string Description { get; }
        JObject InputSchema { get; }
        Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken));
    }

    public static class ToolExecutorExtensions
    {
        public static ToolDefinition ToDefinition(this IToolExecutor tool)
        {
            return new ToolDefinition { Name = tool.Name, Description = tool.Description, InputSchema = tool.InputSchema };
        }
    }
}
