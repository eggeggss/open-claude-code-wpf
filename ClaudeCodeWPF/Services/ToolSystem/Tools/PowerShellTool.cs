using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    /// <summary>
    /// 執行 PowerShell 指令的工具 — 適用於 Windows 環境操作
    /// </summary>
    public class PowerShellTool : IToolExecutor
    {
        public string Name => "PowerShell";

        public string Description =>
            "Execute a PowerShell command or script on the local Windows computer. " +
            "Use this to manage files, query system info, run programs, interact with Windows APIs, " +
            "or perform any task that requires PowerShell. Returns stdout and stderr combined.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""command"": {
                    ""type"": ""string"",
                    ""description"": ""The PowerShell command or script to execute""
                },
                ""timeout"": {
                    ""type"": ""integer"",
                    ""description"": ""Timeout in milliseconds (default 30000, max 300000)""
                },
                ""workingDir"": {
                    ""type"": ""string"",
                    ""description"": ""Working directory for the command (optional)""
                }
            },
            ""required"": [""command""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var command = input["command"]?.ToString();
            if (string.IsNullOrEmpty(command))
                return ToolResult.Failure("command is required");

            var timeoutMs = Math.Min(input["timeout"]?.Value<int>() ?? 30000, 300000);
            var workingDir = input["workingDir"]?.ToString() ?? Environment.CurrentDirectory;

            try
            {
                var (output, exitCode) = await RunPowerShellAsync(command, workingDir, timeoutMs, cancellationToken);
                if (exitCode == 0)
                    return ToolResult.Success(string.IsNullOrEmpty(output) ? "(no output)" : output);
                else
                    return ToolResult.Success($"Exit code: {exitCode}\n{output}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure($"PowerShell execution failed: {ex.Message}");
            }
        }

        private static async Task<(string Output, int ExitCode)> RunPowerShellAsync(
            string command,
            string workingDir,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<(string, int)>();

            // Prefer pwsh (PowerShell 7+), fall back to Windows PowerShell 5.x
            var psExe = FindPowerShell();

            // -NoProfile: skip profile scripts for speed
            // -NonInteractive: don't prompt for input
            // -OutputFormat Text: plain text output
            // -Command: the command to run
            var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

            var psi = new ProcessStartInfo
            {
                FileName = psExe,
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

            process.Exited += (s, e) =>
            {
                var combined = outputBuilder.ToString();
                if (errorBuilder.Length > 0)
                    combined += (combined.Length > 0 ? "\nSTDERR:\n" : "STDERR:\n") + errorBuilder.ToString();
                tcs.TrySetResult((combined.TrimEnd(), process.ExitCode));
                process.Dispose();
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                tcs.TrySetCanceled();
            }))
            using (var timeoutCts = new CancellationTokenSource(timeoutMs))
            using (timeoutCts.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                tcs.TrySetResult(($"Error: PowerShell command timed out after {timeoutMs}ms", -1));
            }))
            {
                return await tcs.Task;
            }
        }

        private static string FindPowerShell()
        {
            // Try PowerShell 7+ first
            var pwsh7 = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell", "7", "pwsh.exe");

            if (System.IO.File.Exists(pwsh7))
                return pwsh7;

            // Fall back to Windows PowerShell 5.x
            return "powershell.exe";
        }
    }
}
