using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.Providers
{
    /// <summary>
    /// Azure OpenAI 多節點供應商。
    /// 每個節點對應一個 ModelInfo；ChatAsync 依 CurrentModel 查找正確節點。
    /// POST https://{endpoint}/openai/deployments/{deployment}/chat/completions?api-version=…
    /// </summary>
    public class AzureOpenAIProvider : OpenAIProvider
    {
        public override string ProviderName => "AzureOpenAI";

        public override bool IsAvailable => _config.GetParsedAzureNodes().Count > 0;

        // These are used by the base OpenAIProvider; we override them per-request
        protected override string ApiKey    => GetCurrentNode()?.ApiKey     ?? "";
        protected override string BaseUrl   => BuildBaseUrl(GetCurrentNode());
        protected override string DefaultModel => GetCurrentNode()?.Deployment ?? "";

        private AzureNodeConfig GetCurrentNode()
        {
            var nodes = _config.GetParsedAzureNodes();
            if (nodes.Count == 0) return null;
            var current = _config.CurrentModel;
            return nodes.FirstOrDefault(n => n.Name == current) ?? nodes[0];
        }

        private string BuildBaseUrl(AzureNodeConfig node)
        {
            if (node == null) return "";
            var ep = node.Endpoint.TrimEnd('/');
            // Return base URL without /chat/completions, parent class will append it
            return $"{ep}/openai/deployments/{node.Deployment}";
        }

        private string GetApiVersion(AzureNodeConfig node)
        {
            return node?.ApiVersion ?? "2024-02-01";
        }

        protected override HttpClient CreateClient()
        {
            var node   = GetCurrentNode();
            var client = Utils.HttpClientFactory.Create(_config.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("api-key", node?.ApiKey ?? "");
            return client;
        }

        protected override JObject BuildRequestBody(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            List<ToolDefinition> tools,
            bool stream)
        {
            var body = base.BuildRequestBody(messages, systemPrompt, parameters, tools, stream);
            body.Remove("model"); // deployment is in the URL, not the body
            return body;
        }

        public override async Task<ModelResponse> SendMessageAsync(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            List<ToolDefinition> tools = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var node = GetCurrentNode();
            var client = CreateClient();
            var body = BuildRequestBody(messages, systemPrompt, parameters, tools, stream: false);
            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{BaseUrl}/chat/completions?api-version={GetApiVersion(node)}";

            for (int retry = 0; retry <= _config.MaxRetries; retry++)
            {
                try
                {
                    var response = await client.PostAsync(url, content, cancellationToken);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode == 429 && retry < _config.MaxRetries)
                        {
                            await Task.Delay(1000 * (int)Math.Pow(2, retry), cancellationToken);
                            continue;
                        }
                        throw new Exception($"Azure OpenAI error {response.StatusCode}: {responseText}");
                    }

                    return ParseOpenAIResponse(JObject.Parse(responseText));
                }
                catch (TaskCanceledException) { throw; }
                catch (Exception) when (retry < _config.MaxRetries)
                {
                    await Task.Delay(1000 * (int)Math.Pow(2, retry), cancellationToken);
                }
            }
            throw new Exception($"{ProviderName}: max retries exceeded");
        }

        public override async Task SendMessageStreamAsync(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            Action<StreamEvent> onEvent,
            List<ToolDefinition> tools = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var node = GetCurrentNode();
            var client = CreateClient();
            var body = BuildRequestBody(messages, systemPrompt, parameters, tools, stream: true);
            var json = JsonConvert.SerializeObject(body);
            var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{BaseUrl}/chat/completions?api-version={GetApiVersion(node)}";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = requestContent
            };

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                onEvent(StreamEvent.ErrorEvent($"Azure OpenAI error {response.StatusCode}: {err}"));
                return;
            }

            // Use base class streaming logic (copied from OpenAIProvider)
            var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();

            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (!line.StartsWith("data: ")) continue;

                    var data = line.Substring(6).Trim();
                    if (data == "[DONE]")
                    {
                        foreach (var kv in toolCallBuilders)
                        {
                            var tc = kv.Value.Build();
                            onEvent(new StreamEvent { Type = StreamEventType.ToolCallComplete, ToolCall = tc });
                        }
                        onEvent(StreamEvent.MessageEndEvent("stop"));
                        break;
                    }

                    JObject evt;
                    try { evt = JObject.Parse(data); }
                    catch { continue; }

                    var choices = evt["choices"] as JArray;
                    if (choices == null || choices.Count == 0) continue;

                    var delta = choices[0]["delta"] as JObject;
                    if (delta == null) continue;

                    var textContent = delta["content"]?.ToString();
                    if (!string.IsNullOrEmpty(textContent))
                        onEvent(StreamEvent.TextDeltaEvent(textContent));

                    var toolCallsArr = delta["tool_calls"] as JArray;
                    if (toolCallsArr != null)
                    {
                        foreach (var tcDelta in toolCallsArr)
                        {
                            int idx = tcDelta["index"]?.Value<int>() ?? 0;
                            if (!toolCallBuilders.ContainsKey(idx))
                            {
                                var id = tcDelta["id"]?.ToString() ?? Guid.NewGuid().ToString();
                                var name = tcDelta["function"]?["name"]?.ToString() ?? "";
                                toolCallBuilders[idx] = new ToolCallBuilder { Id = id, Name = name };
                                onEvent(new StreamEvent
                                {
                                    Type = StreamEventType.ToolCallStart,
                                    ToolCall = new ToolCall { Id = id, Name = name }
                                });
                            }
                            var argsDelta = tcDelta["function"]?["arguments"]?.ToString() ?? "";
                            toolCallBuilders[idx].ArgsBuffer.Append(argsDelta);
                            if (!string.IsNullOrEmpty(argsDelta))
                                onEvent(new StreamEvent { Type = StreamEventType.ToolCallDelta, Text = argsDelta });
                        }
                    }

                    var usageObj = evt["usage"] as JObject;
                    if (usageObj != null)
                    {
                        onEvent(StreamEvent.UsageEvent(new TokenUsage
                        {
                            InputTokens = usageObj["prompt_tokens"]?.Value<int>() ?? 0,
                            OutputTokens = usageObj["completion_tokens"]?.Value<int>() ?? 0
                        }));
                    }
                }
            }
        }

        private class ToolCallBuilder
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public StringBuilder ArgsBuffer { get; set; } = new StringBuilder();

            public ToolCall Build()
            {
                JObject args;
                try { args = JObject.Parse(ArgsBuffer.ToString()); }
                catch { args = new JObject(); }
                return new ToolCall { Id = Id, Name = Name, Arguments = args };
            }
        }

        public override Task<List<ModelInfo>> GetAvailableModelsAsync()
        {
            var result = _config.GetParsedAzureNodes()
                .Select(n => new ModelInfo
                {
                    Id           = n.Name,
                    DisplayName  = $"{n.Name} ({n.Deployment})",
                    Provider     = ProviderName,
                    MaxTokens    = 16384,
                    SupportsTools = true
                })
                .ToList();

            if (result.Count == 0)
                result.Add(new ModelInfo
                {
                    Id = "(未設定)", DisplayName = "Azure OpenAI（未設定節點）",
                    Provider = ProviderName, MaxTokens = 16384
                });

            return Task.FromResult(result);
        }
    }
}
