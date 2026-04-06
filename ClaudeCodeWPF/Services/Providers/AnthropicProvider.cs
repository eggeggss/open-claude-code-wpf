using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.Providers
{
    /// <summary>
    /// Anthropic Claude API 供應商 (Direct API)
    /// POST https://api.anthropic.com/v1/messages
    /// </summary>
    public class AnthropicProvider : IModelProvider
    {
        private readonly ConfigService _config;
        private readonly FunctionCallingAdapter _fcAdapter;

        public string ProviderName => "Anthropic";
        public bool IsAvailable => !string.IsNullOrEmpty(_config.GetApiKeyForProvider("Anthropic"));

        public AnthropicProvider()
        {
            _config = ConfigService.Instance;
            _fcAdapter = new FunctionCallingAdapter();
        }

        public async Task<ModelResponse> SendMessageAsync(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            List<ToolDefinition> tools = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var client = Utils.HttpClientFactory.Create(_config.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("x-api-key", _config.GetApiKeyForProvider("Anthropic"));
            client.DefaultRequestHeaders.Add("anthropic-version", _config.AnthropicApiVersion);

            var body = BuildRequestBody(messages, systemPrompt, parameters, tools, stream: false);
            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            for (int retry = 0; retry <= _config.MaxRetries; retry++)
            {
                try
                {
                    var response = await client.PostAsync($"{_config.GetBaseUrlForProvider("Anthropic")}/v1/messages", content, cancellationToken);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        if (response.StatusCode == (System.Net.HttpStatusCode)429 && retry < _config.MaxRetries)
                        {
                            await Task.Delay(1000 * (int)Math.Pow(2, retry), cancellationToken);
                            continue;
                        }
                        throw new Exception($"Anthropic API error {response.StatusCode}: {responseText}");
                    }

                    return ParseAnthropicResponse(JObject.Parse(responseText));
                }
                catch (TaskCanceledException) { throw; }
                catch (Exception) when (retry < _config.MaxRetries)
                {
                    await Task.Delay(1000 * (int)Math.Pow(2, retry), cancellationToken);
                }
            }
            throw new Exception("Anthropic: max retries exceeded");
        }

        public async Task SendMessageStreamAsync(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            Action<StreamEvent> onEvent,
            List<ToolDefinition> tools = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var client = Utils.HttpClientFactory.Create(_config.TimeoutSeconds);
            client.DefaultRequestHeaders.Add("x-api-key", _config.GetApiKeyForProvider("Anthropic"));
            client.DefaultRequestHeaders.Add("anthropic-version", _config.AnthropicApiVersion);

            var body = BuildRequestBody(messages, systemPrompt, parameters, tools, stream: true);
            var json = JsonConvert.SerializeObject(body);
            var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_config.GetBaseUrlForProvider("Anthropic")}/v1/messages")
            {
                Content = requestContent
            };

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                onEvent(StreamEvent.ErrorEvent($"Anthropic error {response.StatusCode}: {err}"));
                return;
            }

            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream))
            {
                string currentToolId = null;
                string currentToolName = null;
                var currentToolArgs = new StringBuilder();
                int currentToolIndex = -1;
                var toolCalls = new List<ToolCall>();

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (!line.StartsWith("data: ")) continue;

                    var data = line.Substring(6);
                    if (data == "[DONE]") break;

                    JObject evt;
                    try { evt = JObject.Parse(data); }
                    catch { continue; }

                    var type = evt["type"]?.ToString();

                    switch (type)
                    {
                        case "message_start":
                            var usage = evt["message"]?["usage"];
                            if (usage != null)
                            {
                                onEvent(StreamEvent.UsageEvent(new TokenUsage
                                {
                                    InputTokens = usage["input_tokens"]?.Value<int>() ?? 0
                                }));
                            }
                            break;

                        case "content_block_start":
                            var block = evt["content_block"];
                            if (block?["type"]?.ToString() == "tool_use")
                            {
                                currentToolIndex = evt["index"]?.Value<int>() ?? 0;
                                currentToolId = block["id"]?.ToString();
                                currentToolName = block["name"]?.ToString();
                                currentToolArgs.Clear();
                                onEvent(new StreamEvent
                                {
                                    Type = StreamEventType.ToolCallStart,
                                    ToolCall = new ToolCall { Id = currentToolId, Name = currentToolName }
                                });
                            }
                            break;

                        case "content_block_delta":
                            var delta = evt["delta"];
                            var deltaType = delta?["type"]?.ToString();
                            if (deltaType == "text_delta")
                            {
                                onEvent(StreamEvent.TextDeltaEvent(delta["text"]?.ToString() ?? ""));
                            }
                            else if (deltaType == "thinking_delta")
                            {
                                onEvent(StreamEvent.ThinkingDeltaEvent(delta["thinking"]?.ToString() ?? ""));
                            }
                            else if (deltaType == "input_json_delta")
                            {
                                currentToolArgs.Append(delta["partial_json"]?.ToString() ?? "");
                                onEvent(new StreamEvent
                                {
                                    Type = StreamEventType.ToolCallDelta,
                                    Text = delta["partial_json"]?.ToString() ?? ""
                                });
                            }
                            break;

                        case "content_block_stop":
                            if (currentToolId != null)
                            {
                                JObject args = null;
                                try { args = JObject.Parse(currentToolArgs.ToString()); }
                                catch { args = new JObject(); }
                                var tc = new ToolCall { Id = currentToolId, Name = currentToolName, Arguments = args };
                                toolCalls.Add(tc);
                                onEvent(new StreamEvent { Type = StreamEventType.ToolCallComplete, ToolCall = tc });
                                currentToolId = null;
                                currentToolName = null;
                            }
                            break;

                        case "message_delta":
                            var deltaUsage = evt["usage"];
                            var stopReason = evt["delta"]?["stop_reason"]?.ToString();
                            var finalUsage = deltaUsage != null ? new TokenUsage
                            {
                                OutputTokens = deltaUsage["output_tokens"]?.Value<int>() ?? 0
                            } : null;
                            onEvent(StreamEvent.MessageEndEvent(stopReason, finalUsage));
                            break;
                    }
                }
            }
        }

        public Task<List<ModelInfo>> GetAvailableModelsAsync()
        {
            return Task.FromResult(new List<ModelInfo>
            {
                new ModelInfo { Id = "claude-opus-4-20250514",   DisplayName = "Claude Opus 4",      Provider = ProviderName, MaxTokens = 32000, SupportsTools = true, SupportsVision = true, SupportsThinking = true },
                new ModelInfo { Id = "claude-sonnet-4-20250514", DisplayName = "Claude Sonnet 4",    Provider = ProviderName, MaxTokens = 16000, SupportsTools = true, SupportsVision = true, SupportsThinking = true },
                new ModelInfo { Id = "claude-haiku-4-20250514",  DisplayName = "Claude Haiku 4.5",   Provider = ProviderName, MaxTokens = 8192,  SupportsTools = true, SupportsVision = true },
                new ModelInfo { Id = "claude-opus-4-5",          DisplayName = "Claude Opus 4.5",    Provider = ProviderName, MaxTokens = 32000, SupportsTools = true, SupportsVision = true, SupportsThinking = true },
                new ModelInfo { Id = "claude-sonnet-4-5",        DisplayName = "Claude Sonnet 4.5",  Provider = ProviderName, MaxTokens = 16000, SupportsTools = true, SupportsVision = true, SupportsThinking = true },
            });
        }

        private JObject BuildRequestBody(List<ChatMessage> messages, string systemPrompt, ModelParameters parameters, List<ToolDefinition> tools, bool stream)
        {
            var body = new JObject
            {
                ["model"] = parameters?.Model ?? _config.AnthropicDefaultModel,
                ["max_tokens"] = parameters?.MaxTokens ?? _config.MaxTokens,
                ["stream"] = stream
            };

            if (!string.IsNullOrEmpty(systemPrompt))
                body["system"] = systemPrompt;

            if (parameters?.Temperature > 0)
                body["temperature"] = parameters.Temperature;

            if (parameters?.ThinkingBudget > 0)
            {
                body["thinking"] = new JObject
                {
                    ["type"] = "enabled",
                    ["budget_tokens"] = parameters.ThinkingBudget
                };
            }

            body["messages"] = _fcAdapter.BuildAnthropicMessages(messages);

            if (tools != null && tools.Count > 0)
                body["tools"] = _fcAdapter.ToAnthropicTools(tools);

            return body;
        }

        private ModelResponse ParseAnthropicResponse(JObject obj)
        {
            var response = new ModelResponse
            {
                StopReason = obj["stop_reason"]?.ToString()
            };

            var usageObj = obj["usage"];
            if (usageObj != null)
            {
                response.Usage = new TokenUsage
                {
                    InputTokens = usageObj["input_tokens"]?.Value<int>() ?? 0,
                    OutputTokens = usageObj["output_tokens"]?.Value<int>() ?? 0,
                    CacheReadInputTokens = usageObj["cache_read_input_tokens"]?.Value<int>() ?? 0,
                    CacheCreationInputTokens = usageObj["cache_creation_input_tokens"]?.Value<int>() ?? 0
                };
            }

            var content = obj["content"] as JArray;
            if (content != null)
            {
                var textParts = new StringBuilder();
                var toolCalls = new List<ToolCall>();

                foreach (var block in content)
                {
                    var blockType = block["type"]?.ToString();
                    if (blockType == "text")
                        textParts.Append(block["text"]?.ToString() ?? "");
                    else if (blockType == "tool_use")
                    {
                        JObject args = null;
                        try { args = (JObject)block["input"]; }
                        catch { args = new JObject(); }

                        toolCalls.Add(new ToolCall
                        {
                            Id = block["id"]?.ToString(),
                            Name = block["name"]?.ToString(),
                            Arguments = args ?? new JObject()
                        });
                    }
                }

                response.Content = textParts.ToString();
                if (toolCalls.Count > 0) response.ToolCalls = toolCalls;
            }

            return response;
        }
    }
}
