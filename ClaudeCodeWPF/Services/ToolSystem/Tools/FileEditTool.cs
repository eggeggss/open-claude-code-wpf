using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Utils;
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

                // Normalize old_string line endings to match the file's actual line endings
                // (model sends \n from JSON; files may use \r\n on Windows)
                var fileHasCrlf = original.Contains("\r\n");
                var normalizedOld = NormalizeLineEndings(oldStr, fileHasCrlf);
                var normalizedNew = NormalizeLineEndings(newStr, fileHasCrlf);

                if (!original.Contains(normalizedOld))
                    return ToolResult.Failure($"old_string not found in file. Make sure it matches exactly.");

                var count = CountOccurrences(original, normalizedOld);
                if (count > 1)
                    return ToolResult.Failure($"old_string found {count} times. It must be unique. Add more context to make it unique.");

                var updated = original.Replace(normalizedOld, normalizedNew);

                await Task.Run(() => File.WriteAllText(path, updated), cancellationToken);

                return ToolResult.Success($"Successfully edited {path}\n\nReplaced:\n{normalizedOld}\n\nWith:\n{normalizedNew}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
            }
        }

        private static string NormalizeLineEndings(string text, bool toCrlf)
        {
            // Normalize to LF first, then convert if needed
            var lf = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return toCrlf ? lf.Replace("\n", "\r\n") : lf;
        }

        private int CountOccurrences(string text, string pattern)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) != -1) { count++; idx++; }
            return count;
        }

        private static string ResolvePath(string path) => PathHelper.Resolve(path);
    }
}