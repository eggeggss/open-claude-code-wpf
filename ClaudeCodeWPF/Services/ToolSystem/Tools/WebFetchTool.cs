using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Utils;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class WebFetchTool : IToolExecutor
    {
        public string Name => "WebFetch";
        public string Description => "Fetch the content of a URL. Returns the HTTP response body (text).";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""url"": { ""type"": ""string"", ""description"": ""The URL to fetch"" },
                ""max_length"": { ""type"": ""integer"", ""description"": ""Maximum characters to return (default: 20000)"" }
            },
            ""required"": [""url""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var url = input["url"]?.ToString();
            if (string.IsNullOrEmpty(url)) return ToolResult.Failure("url is required");

            var maxLength = input["max_length"]?.Value<int>() ?? 20000;

            try
            {
                using (var client = HttpClientFactory.Create())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 ClaudeCodeWPF/1.0");
                    var response = await client.GetStringAsync(url);
                    if (response.Length > maxLength)
                        response = response.Substring(0, maxLength) + "\n...[truncated]";
                    return ToolResult.Success(response);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
            }
        }
    }
}
