using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class GrepTool : IToolExecutor
    {
        public string Name => "Grep";
        public string Description => "Search for a pattern (regex or literal) in files. Supports recursive directory search with file extension filtering.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""pattern"": { ""type"": ""string"", ""description"": ""Regex or literal pattern to search for"" },
                ""path"": { ""type"": ""string"", ""description"": ""File or directory to search in"" },
                ""include"": { ""type"": ""string"", ""description"": ""Glob pattern to filter files (e.g., *.cs)"" },
                ""ignore_case"": { ""type"": ""boolean"", ""description"": ""Case insensitive search"" },
                ""recursive"": { ""type"": ""boolean"", ""description"": ""Search recursively in directories (default: true)"" },
                ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum number of results (default: 100)"" }
            },
            ""required"": [""pattern""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var pattern = input["pattern"]?.ToString();
            if (string.IsNullOrEmpty(pattern)) return ToolResult.Failure("pattern is required");

            var searchPath = input["path"]?.ToString() ?? Environment.CurrentDirectory;
            searchPath = ResolvePath(searchPath);

            var include = input["include"]?.ToString() ?? "*";
            var ignoreCase = input["ignore_case"]?.Value<bool>() ?? false;
            var recursive = input["recursive"]?.Value<bool>() ?? true;
            var maxResults = input["max_results"]?.Value<int>() ?? 100;

            var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            Regex regex;
            try { regex = new Regex(pattern, options); }
            catch { regex = new Regex(Regex.Escape(pattern), options); }

            try
            {
                var results = new List<string>();
                await Task.Run(() => SearchFiles(searchPath, include, regex, recursive, maxResults, results, cancellationToken), cancellationToken);

                if (results.Count == 0)
                    return ToolResult.Success("No matches found.");

                return ToolResult.Success(string.Join("\n", results));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
            }
        }

        private void SearchFiles(string searchPath, string include, Regex regex, bool recursive, int maxResults, List<string> results, CancellationToken cancellationToken)
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(searchPath, include, searchOption);

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (results.Count >= maxResults) break;

                try
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            results.Add($"{file}:{i + 1}: {lines[i]}");
                            if (results.Count >= maxResults) return;
                        }
                    }
                }
                catch { /* skip unreadable files */ }
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
