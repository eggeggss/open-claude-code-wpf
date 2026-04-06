using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Utils;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class WebSearchTool : IToolExecutor
    {
        public string Name => "WebSearch";
        public string Description => "Search the web using a search query. Returns search results snippets.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""query"": { ""type"": ""string"", ""description"": ""The search query"" },
                ""max_results"": { ""type"": ""integer"", ""description"": ""Maximum results to return (default: 10)"" }
            },
            ""required"": [""query""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken cancellationToken = default(CancellationToken))
        {
            var query = input["query"]?.ToString();
            if (string.IsNullOrEmpty(query)) return ToolResult.Failure("query is required");

            // Use DuckDuckGo HTML (no API key required)
            var encoded = Uri.EscapeUriString(query);
            var url = $"https://html.duckduckgo.com/html/?q={encoded}";

            try
            {
                using (var client = HttpClientFactory.Create())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 ClaudeCodeWPF/1.0");
                    var html = await client.GetStringAsync(url);
                    // Extract result snippets (simple HTML parsing)
                    var snippets = ExtractResults(html, input["max_results"]?.Value<int>() ?? 10);
                    return ToolResult.Success(snippets);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ToolResult.Failure(ex.Message);
            }
        }

        private string ExtractResults(string html, int maxResults)
        {
            var sb = new System.Text.StringBuilder();
            var matches = System.Text.RegularExpressions.Regex.Matches(
                html,
                @"<a class=""result__a"" href=""([^""]+)""[^>]*>([^<]+)</a>.*?<a class=""result__snippet""[^>]*>([^<]+)</a>",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            int count = 0;
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (count >= maxResults) break;
                sb.AppendLine($"{count + 1}. {m.Groups[2].Value.Trim()}");
                sb.AppendLine($"   URL: {m.Groups[1].Value.Trim()}");
                sb.AppendLine($"   {m.Groups[3].Value.Trim()}");
                sb.AppendLine();
                count++;
            }

            return count == 0 ? "No results found." : sb.ToString().TrimEnd();
        }
    }
}
