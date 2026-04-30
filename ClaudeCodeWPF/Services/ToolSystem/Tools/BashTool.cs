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
        public string Description => "Execute a Windows cmd.exe command. Returns stdout and stderr combined. " +
            "IMPORTANT: This runs cmd.exe on Windows — heredoc syntax (<<EOF, <<'PY') is NOT supported. " +
            "For multi-line scripts, use the PowerShell tool instead. " +
            "For Python scripts, write to a temp file first or use PowerShell.";

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

            var timeout = Math.Max(5000, input["timeout"]?.Value<int>() ?? 30000);
            var workingDir = PathHelper.ResolveWorkingDir(input["workingDir"]?.ToString());

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
