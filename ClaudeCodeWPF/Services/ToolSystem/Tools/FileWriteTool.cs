using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class FileWriteTool : IToolExecutor
    {
        public string Name => "Write";
        public string Description => "Write or overwrite a file with the provided content. Creates the file and any necessary parent directories if they don't exist.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""file_path"": { ""type"": ""string"", ""description"": ""Absolute or relative path to the file"" },
                ""content"": { ""type"": ""string"", ""description"": ""The content to write to the file"" },
                ""append"": { ""type"": ""boolean"", ""description"": ""If true, append instead of overwrite (default: false)"" }
            },
            ""required"": [""file_path"", ""content""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var path = input["file_path"]?.ToString();
            var content = input["content"]?.ToString();
            if (string.IsNullOrEmpty(path)) return ToolResult.Failure("file_path is required");
            if (content == null) return ToolResult.Failure("content is required");

            path = ResolvePath(path);
            var append = input["append"]?.Value<bool>() ?? false;

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                await Task.Run(() =>
                {
                    if (append) File.AppendAllText(path, content);
                    else File.WriteAllText(path, content);
                }, cancellationToken);

                return ToolResult.Success($"Successfully {(append ? "appended to" : "wrote")} file: {path}");
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
