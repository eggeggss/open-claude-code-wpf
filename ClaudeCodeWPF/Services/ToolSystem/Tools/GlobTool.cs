using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class GlobTool : IToolExecutor
    {
        public string Name => "Glob";
        public string Description => "Find files matching a glob pattern. Returns matching file paths.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""pattern"": { ""type"": ""string"", ""description"": ""Glob pattern (e.g., **/*.cs, src/**/*.ts)"" },
                ""path"": { ""type"": ""string"", ""description"": ""Base directory to search from"" },
                ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum number of results (default: 200)"" }
            },
            ""required"": [""pattern""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var pattern = input["pattern"]?.ToString();
            if (string.IsNullOrEmpty(pattern)) return ToolResult.Failure("pattern is required");

            var basePath = input["path"]?.ToString() ?? Environment.CurrentDirectory;
            basePath = ResolvePath(basePath);
            var maxResults = input["max_results"]?.Value<int>() ?? 200;

            try
            {
                var matches = await Task.Run(() => GlobSearch(basePath, pattern, maxResults, cancellationToken), cancellationToken);

                if (matches.Count == 0)
                    return ToolResult.Success("No files matched the pattern.");

                return ToolResult.Success(string.Join("\n", matches));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
            }
        }

        private List<string> GlobSearch(string basePath, string pattern, int maxResults, CancellationToken cancellationToken)
        {
            // Convert glob to search pattern
            var results = new List<string>();

            // Simple glob: split by directory separator for ** support
            var parts = pattern.Replace('\\', '/').Split('/');
            SearchRecursive(basePath, parts, 0, results, maxResults, cancellationToken);
            return results;
        }

        private void SearchRecursive(string currentPath, string[] parts, int partIndex, List<string> results, int maxResults, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(currentPath) || results.Count >= maxResults) return;
            if (cancellationToken.IsCancellationRequested) return;

            if (partIndex >= parts.Length) return;

            var part = parts[partIndex];
            bool isLast = partIndex == parts.Length - 1;

            if (part == "**")
            {
                // Match any number of directories
                if (isLast)
                {
                    foreach (var file in Directory.GetFiles(currentPath, "*", SearchOption.AllDirectories))
                    {
                        results.Add(file);
                        if (results.Count >= maxResults) return;
                    }
                }
                else
                {
                    // ** followed by more parts
                    SearchRecursive(currentPath, parts, partIndex + 1, results, maxResults, cancellationToken);
                    foreach (var dir in Directory.GetDirectories(currentPath))
                    {
                        SearchRecursive(dir, parts, partIndex, results, maxResults, cancellationToken);
                    }
                }
            }
            else if (isLast)
            {
                // Last part — match files
                var regexPattern = "^" + Regex.Escape(part).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                foreach (var file in Directory.GetFiles(currentPath))
                {
                    if (Regex.IsMatch(Path.GetFileName(file), regexPattern, RegexOptions.IgnoreCase))
                    {
                        results.Add(file);
                        if (results.Count >= maxResults) return;
                    }
                }
            }
            else
            {
                // Directory pattern
                var regexPattern = "^" + Regex.Escape(part).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                foreach (var dir in Directory.GetDirectories(currentPath))
                {
                    if (Regex.IsMatch(Path.GetFileName(dir), regexPattern, RegexOptions.IgnoreCase))
                    {
                        SearchRecursive(dir, parts, partIndex + 1, results, maxResults, cancellationToken);
                    }
                }
            }
        }

        private string ResolvePath(string path)
        {
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Environment.CurrentDirectory, path);
            return Path.GetFullPath(path);
        }
    }
}
