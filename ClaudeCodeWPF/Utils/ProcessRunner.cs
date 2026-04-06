using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClaudeCodeWPF.Utils
{
    public static class ProcessRunner
    {
        /// <summary>
        /// Run a shell command and return (combined_output, exit_code)
        /// </summary>
        public static async Task<(string Output, int ExitCode)> RunAsync(
            string command,
            string workingDir = null,
            int timeoutMs = 30000,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tcs = new TaskCompletionSource<(string, int)>();

            var psi = new ProcessStartInfo
            {
                FileName = GetShell(),
                Arguments = GetShellArgs(command),
                WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
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
                    combined += (combined.Length > 0 ? "\n" : "") + errorBuilder.ToString();
                tcs.TrySetResult((combined.TrimEnd(), process.ExitCode));
                process.Dispose();
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Register cancellation
            using (cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                tcs.TrySetCanceled();
            }))
            using (var timeoutCts = new CancellationTokenSource(timeoutMs))
            using (timeoutCts.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                tcs.TrySetResult(("Error: Command timed out", -1));
            }))
            {
                return await tcs.Task;
            }
        }

        private static string GetShell()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return "cmd.exe";
            return "/bin/bash";
        }

        private static string GetShellArgs(string command)
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                return $"/c {command}";
            return $"-c \"{command.Replace("\"", "\\\"")}\"";
        }
    }
}
