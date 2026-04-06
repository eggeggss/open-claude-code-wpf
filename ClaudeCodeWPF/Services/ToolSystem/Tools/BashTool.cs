using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Utils;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class BashTool : IToolExecutor
    {
        public string Name => "Bash";
        public string Description => "Execute a bash/shell command in the current working directory. Returns stdout and stderr combined. For long-running commands, consider a timeout.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""command"": { ""type"": ""string"", ""description"": ""The shell command to run"" },
                ""timeout"": { ""type"": ""integer"", ""description"": ""Timeout in milliseconds (default 30000)"" },
                ""workingDir"": { ""type"": ""string"", ""description"": ""Working directory for the command"" }
            },
            ""required"": [""command""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var command = input["command"]?.ToString();
            if (string.IsNullOrEmpty(command))
                return ToolResult.Failure("command is required");

            var timeout = input["timeout"]?.Value<int>() ?? 30000;
            var workingDir = input["workingDir"]?.ToString() ?? Environment.CurrentDirectory;

            try
            {
                var (output, exitCode) = await ProcessRunner.RunAsync(command, workingDir, timeout, cancellationToken);
                if (exitCode == 0)
                    return ToolResult.Success(output);
                else
                    return ToolResult.Success($"Exit code: {exitCode}\n{output}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
            }
        }
    }
}
