using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    /// <summary>
    /// 開啟 Microsoft Edge 瀏覽器並導航到指定 URL
    /// </summary>
    public class OpenEdgeTool : IToolExecutor
    {
        public string Name => "OpenEdge";

        public string Description =>
            "Open Microsoft Edge browser and navigate to a URL. " +
            "Use this to open web pages, documentation, online tools, or any website in Edge. " +
            "If no URL is provided, opens Edge's new tab page.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""url"": {
                    ""type"": ""string"",
                    ""description"": ""The URL to open in Edge (e.g. https://example.com). If omitted, opens a new tab.""
                },
                ""newWindow"": {
                    ""type"": ""boolean"",
                    ""description"": ""Open in a new window instead of a new tab (default: false)""
                }
            },
            ""required"": []
        }");

        public Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var url = input["url"]?.ToString()?.Trim();
            var newWindow = input["newWindow"]?.Value<bool>() ?? false;

            // Default to new tab page if no URL
            if (string.IsNullOrEmpty(url))
                url = "edge://newtab";

            // Basic URL validation — add https:// if scheme is missing
            if (!url.StartsWith("http://") && !url.StartsWith("https://") &&
                !url.StartsWith("edge://") && !url.StartsWith("file://"))
            {
                url = "https://" + url;
            }

            try
            {
                var edgePath = FindEdge();
                var args = newWindow ? $"--new-window \"{url}\"" : $"--new-tab \"{url}\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = edgePath,
                    Arguments = args,
                    UseShellExecute = false
                });

                return Task.FromResult(ToolResult.Success($"已在 Edge 開啟：{url}"));
            }
            catch (Exception ex)
            {
                // Fallback: use ShellExecute which lets Windows pick the default handler
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                    return Task.FromResult(ToolResult.Success($"已開啟：{url}"));
                }
                catch
                {
                    return Task.FromResult(ToolResult.Failure($"無法開啟 Edge：{ex.Message}"));
                }
            }
        }

        private static string FindEdge()
        {
            // Standard Edge installation paths
            var candidates = new[]
            {
                // Per-user install
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\Application\msedge.exe"),
                // System-wide install
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    @"Microsoft\Edge\Application\msedge.exe"),
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Microsoft\Edge\Application\msedge.exe"),
            };

            foreach (var path in candidates)
                if (System.IO.File.Exists(path))
                    return path;

            // Fall back to the command name (works if Edge is in PATH)
            return "msedge.exe";
        }
    }
}
