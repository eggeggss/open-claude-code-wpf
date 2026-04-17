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
    /// Azure OpenAI Responses API (for GPT-5.x series)
    /// POST https://{endpoint}/openai/responses?api-version=2025-04-01-preview
    /// Uses Bearer token auth instead of api-key
    /// Model is specified in request body, not in URL
    /// </summary>
    public class AzureResponsesProvider : IModelProvider
    {
        protected readonly ConfigService _config;
        protected readonly FunctionCallingAdapter _fcAdapter;

        public virtual string ProviderName => "AzureResponses";

        public virtual bool IsAvailable => _config.GetParsedAzureResponsesNodes().Count > 0;

        public AzureResponsesProvider()
        {
            _config = ConfigService.Instance;
            _fcAdapter = new FunctionCallingAdapter();
        }

        private AzureResponsesNodeConfig GetCurrentNode()
        {
            var nodes = _config.GetParsedAzureResponsesNodes();
            if (nodes.Count == 0) return null;
            var current = _config.CurrentModel;
            return nodes.FirstOrDefault(n => n.Name == current) ?? nodes[0];
        }

        private string BuildUrl(AzureResponsesNodeConfig node)
        {
            if (node == null) return "";
            var ep = node.Endpoint.TrimEnd('/');
            return $"{ep}/openai/responses?api-version={node.ApiVersion}";
        }

        protected virtual HttpClient CreateClient()
        {
            var node = GetCurrentNode();
            var client = Utils.HttpClientFactory.Create(_config.TimeoutSeconds);
            // Responses API uses Bearer token, not api-key header
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", node?.ApiKey ?? "");
            return client;
        }

        protected virtual JObject BuildRequestBody(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            List<ToolDefinition> tools,
            bool stream)
        {
            var node = GetCurrentNode();
            // Always use node.Model (e.g. "gpt-5.1-codex-mini"), not parameters.Model which may contain the node Name
            var model = node?.Model ?? parameters?.Model ?? "";
            var maxTokens = parameters?.MaxTokens ?? _config.MaxTokens;

            var body = new JObject
            {
                ["model"] = model,
                ["max_output_tokens"] = maxTokens,  // Responses API uses max_output_tokens
                ["stream"] = stream
            };

            // Note: codex models (gpt-5.1-codex-mini etc.) do NOT support temperature
            // Only set temperature for non-codex models
            if (model != null && !model.ToLower().Contains("codex"))
            {
                body["temperature"] = parameters?.Temperature ?? _config.Temperature;
            }

            // Responses API uses "input" with native format (function_call / function_call_output)
            var msgs = _fcAdapter.BuildResponsesApiMessages(messages, systemPrompt);
            body["input"] = msgs;

            if (tools != null && tools.Count > 0)
            {
                // Responses API uses flat tool format (name at top level, not nested in function)
                body["tools"] = _fcAdapter.ToResponsesApiTools(tools);
                body["tool_choice"] = "auto";
            }

            return body;
        }

        public virtual async Task<ModelResponse> SendMessageAsync(
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

            var url = BuildUrl(node);

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
                        throw new Exception($"Azure Responses API error {response.StatusCode}: {responseText}");
                    }

                    return ParseResponse(JObject.Parse(responseText));
                }
                catch (TaskCanceledException) { throw; }
                catch (Exception) when (retry < _config.MaxRetries)
                {
                    await Task.Delay(1000 * (int)Math.Pow(2, retry), cancellationToken);
                }
            }
            throw new Exception($"{ProviderName}: max retries exceeded");
        }

        public virtual async Task SendMessageStreamAsync(
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

            var url = BuildUrl(node);
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = requestContent
            };

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                onEvent(StreamEvent.ErrorEvent($"Azure Responses API error {response.StatusCode}: {err}"));
                return;
            }

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

                    // Responses API uses event types like "response.output_text.delta", "response.completed", etc.
                    var eventType = evt["type"]?.ToString() ?? "";

                    // Handle text delta events
                    if (eventType == "response.output_text.delta")
                    {
                        var textDelta = evt["delta"]?.ToString();
                        if (!string.IsNullOrEmpty(textDelta))
                            onEvent(StreamEvent.TextDeltaEvent(textDelta));
                    }
                    // Handle function call events
                    else if (eventType == "response.function_call_arguments.delta")
                    {
                        var itemId = evt["item_id"]?.ToString() ?? "";
                        var argsDelta = evt["delta"]?.ToString() ?? "";
                        
                        // Find builder by item_id (which matches the function_call item id)
                        var matchedIdx = -1;
                        foreach (var kv in toolCallBuilders)
                        {
                            if (kv.Value.ItemId == itemId)
                            {
                                matchedIdx = kv.Key;
                                break;
                            }
                        }
                        
                        if (matchedIdx >= 0)
                        {
                            toolCallBuilders[matchedIdx].ArgsBuffer.Append(argsDelta);
                            if (!string.IsNullOrEmpty(argsDelta))
                                onEvent(new StreamEvent { Type = StreamEventType.ToolCallDelta, Text = argsDelta });
                        }
                    }
                    // Handle function call start
                    else if (eventType == "response.output_item.added")
                    {
                        var item = evt["item"] as JObject;
                        if (item != null && item["type"]?.ToString() == "function_call")
                        {
                            var itemId = item["id"]?.ToString() ?? "";
                            var callId = item["call_id"]?.ToString() ?? Guid.NewGuid().ToString();
                            var name = item["name"]?.ToString() ?? "";
                            var idx = toolCallBuilders.Count;
                            toolCallBuilders[idx] = new ToolCallBuilder { Id = callId, ItemId = itemId, Name = name };
                            onEvent(new StreamEvent
                            {
                                Type = StreamEventType.ToolCallStart,
                                ToolCall = new ToolCall { Id = callId, Name = name }
                            });
                        }
                    }
                    // Handle completion event
                    else if (eventType == "response.completed")
                    {
                        var responseObj = evt["response"] as JObject;
                        if (responseObj != null)
                        {
                            var usageObj = responseObj["usage"] as JObject;
                            if (usageObj != null)
                            {
                                onEvent(StreamEvent.UsageEvent(new TokenUsage
                                {
                                    InputTokens = usageObj["input_tokens"]?.Value<int>() ?? 0,
                                    OutputTokens = usageObj["output_tokens"]?.Value<int>() ?? 0
                                }));
                            }
                        }
                        
                        // Complete any pending tool calls
                        foreach (var kv in toolCallBuilders)
                        {
                            var tc = kv.Value.Build();
                            onEvent(new StreamEvent { Type = StreamEventType.ToolCallComplete, ToolCall = tc });
                        }
                        onEvent(StreamEvent.MessageEndEvent("stop"));
                        break;
                    }
                }
            }
        }

        protected ModelResponse ParseResponse(JObject obj)
        {
            var response = new ModelResponse();

            // Responses API uses different field names for usage
            var usageObj = obj["usage"] as JObject;
            if (usageObj != null)
            {
                response.Usage = new TokenUsage
                {
                    InputTokens = usageObj["input_tokens"]?.Value<int>() ?? 0,
                    OutputTokens = usageObj["output_tokens"]?.Value<int>() ?? 0
                };
            }

            // Responses API uses "output" array instead of "choices"
            var outputArr = obj["output"] as JArray;
            if (outputArr != null && outputArr.Count > 0)
            {
                var toolCalls = new List<ToolCall>();

                foreach (var item in outputArr)
                {
                    var itemObj = item as JObject;
                    if (itemObj == null) continue;

                    var type = itemObj["type"]?.ToString();

                    if (type == "message")
                    {
                        response.StopReason = itemObj["status"]?.ToString();

                        // Extract text from content array
                        var contentArr = itemObj["content"] as JArray;
                        if (contentArr != null && contentArr.Count > 0)
                        {
                            var firstContent = contentArr[0] as JObject;
                            if (firstContent != null)
                            {
                                response.Content = firstContent["text"]?.ToString() ?? "";
                            }
                        }
                    }
                    else if (type == "function_call")
                    {
                        // Responses API: function_call items are at output level
                        var callId = itemObj["call_id"]?.ToString() ?? Guid.NewGuid().ToString();
                        var name = itemObj["name"]?.ToString() ?? "";
                        var argsStr = itemObj["arguments"]?.ToString() ?? "{}";
                        JObject args;
                        try { args = JObject.Parse(argsStr); }
                        catch { args = new JObject(); }

                        toolCalls.Add(new ToolCall { Id = callId, Name = name, Arguments = args });
                    }
                }

                if (toolCalls.Count > 0)
                {
                    response.ToolCalls = toolCalls;
                    if (string.IsNullOrEmpty(response.StopReason))
                        response.StopReason = "tool_calls";
                }
            }

            return response;
        }

        private class ToolCallBuilder
        {
            public string Id { get; set; }
            public string ItemId { get; set; }  // The item id from response.output_item.added
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

        public virtual Task<List<ModelInfo>> GetAvailableModelsAsync()
        {
            var result = _config.GetParsedAzureResponsesNodes()
                .Select(n => new ModelInfo
                {
                    Id           = n.Name,
                    DisplayName  = $"{n.Name} ({n.Model})",
                    Provider     = ProviderName,
                    MaxTokens    = 16384,
                    SupportsTools = true
                })
                .ToList();

            if (result.Count == 0)
                result.Add(new ModelInfo
                {
                    Id = "(未設定)", DisplayName = "Azure Responses API（未設定節點）",
                    Provider = ProviderName, MaxTokens = 16384
                });

            return Task.FromResult(result);
        }
    }
}
