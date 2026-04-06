using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class FileEditTool : IToolExecutor
    {
        public string Name => "Edit";
        public string Description => "Replace exact text in a file. The old_string must match exactly (including whitespace). Returns a diff of the changes.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""file_path"": { ""type"": ""string"", ""description"": ""Absolute or relative path to the file"" },
                ""old_string"": { ""type"": ""string"", ""description"": ""The exact text to find and replace"" },
                ""new_string"": { ""type"": ""string"", ""description"": ""The replacement text"" }
            },
            ""required"": [""file_path"", ""old_string"", ""new_string""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var path = input["file_path"]?.ToString();
            var oldStr = input["old_string"]?.ToString();
            var newStr = input["new_string"]?.ToString();

            if (string.IsNullOrEmpty(path)) return ToolResult.Failure("file_path is required");
            if (oldStr == null) return ToolResult.Failure("old_string is required");
            if (newStr == null) return ToolResult.Failure("new_string is required");

            path = ResolvePath(path);

            if (!File.Exists(path))
                return ToolResult.Failure($"File not found: {path}");

            try
            {
                var original = await Task.Run(() => File.ReadAllText(path), cancellationToken);

                if (!original.Contains(oldStr))
                    return ToolResult.Failure($"old_string not found in file. Make sure it matches exactly.");

                var count = CountOccurrences(original, oldStr);
                if (count > 1)
                    return ToolResult.Failure($"old_string found {count} times. It must be unique. Add more context to make it unique.");

                var updated = original.Replace(oldStr, newStr);

                await Task.Run(() => File.WriteAllText(path, updated), cancellationToken);

                return ToolResult.Success($"Successfully edited {path}\n\nReplaced:\n{oldStr}\n\nWith:\n{newStr}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
            }
        }

        private int CountOccurrences(string text, string pattern)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) != -1) { count++; idx++; }
            return count;
        }

        private string ResolvePath(string path)
        {
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Environment.CurrentDirectory, path);
            return Path.GetFullPath(path);
        }
    }
}
