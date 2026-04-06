using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class FileReadTool : IToolExecutor
    {
        public string Name => "Read";
        public string Description => "Read the contents of a file from the file system. Returns the file content as text. Supports optional line range.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""file_path"": { ""type"": ""string"", ""description"": ""Absolute or relative path to the file"" },
                ""start_line"": { ""type"": ""integer"", ""description"": ""Start line number (1-based, optional)"" },
                ""end_line"": { ""type"": ""integer"", ""description"": ""End line number (1-based, optional)"" }
            },
            ""required"": [""file_path""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var path = input["file_path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return ToolResult.Failure("file_path is required");

            path = ResolvePath(path);

            if (!File.Exists(path))
                return ToolResult.Failure($"File not found: {path}");

            try
            {
                var lines = await Task.Run(() => File.ReadAllLines(path), cancellationToken);

                int startLine = input["start_line"]?.Value<int>() ?? 1;
                int endLine = input["end_line"]?.Value<int>() ?? lines.Length;

                startLine = Math.Max(1, startLine);
                endLine = Math.Min(lines.Length, endLine);

                var sb = new System.Text.StringBuilder();
                for (int i = startLine - 1; i < endLine; i++)
                {
                    sb.AppendLine($"{i + 1}: {lines[i]}");
                }

                return ToolResult.Success(sb.ToString());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
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
