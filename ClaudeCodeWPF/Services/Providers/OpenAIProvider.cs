using System;
using System.Collections.Generic;
using System.IO;
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
    /// OpenAI GPT API 供應商 (Chat Completions)
    /// POST https://api.openai.com/v1/chat/completions
    /// </summary>
    public class OpenAIProvider : IModelProvider
    {
        protected readonly ConfigService _config;
        protected readonly FunctionCallingAdapter _fcAdapter;

        public virtual string ProviderName => "OpenAI";
        public virtual bool IsAvailable => !string.IsNullOrEmpty(_config.GetApiKeyForProvider(ProviderName));

        protected virtual string ApiKey => _config.GetApiKeyForProvider(ProviderName);
        protected virtual string BaseUrl => _config.GetBaseUrlForProvider(ProviderName);
        protected virtual string DefaultModel => _config.OpenAIDefaultModel;

        public OpenAIProvider()
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
            var client = CreateClient();
            var body = BuildRequestBody(messages, systemPrompt, parameters, tools, stream: false);
            var json = JsonConvert.SerializeObject(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            for (int retry = 0; retry <= _config.MaxRetries; retry++)
            {
                try
                {
                    var response = await client.PostAsync($"{BaseUrl}/chat/completions", content, cancellationToken);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        if ((int)response.StatusCode == 429 && retry < _config.MaxRetries)
                        {
                            await Task.Delay(1000 * (int)Math.Pow(2, retry), cancellationToken);
                            continue;
                        }
                        var errBody = await response.Content.ReadAsStringAsync();
                        throw new Exception(ParseErrorMessage(errBody, response.StatusCode.ToString()));
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

        public async Task SendMessageStreamAsync(
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            Action<StreamEvent> onEvent,
            List<ToolDefinition> tools = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var client = CreateClient();
            var body = BuildRequestBody(messages, systemPrompt, parameters, tools, stream: true);
            var json = JsonConvert.SerializeObject(body);
            var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
            {
                Content = requestContent
            };

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                onEvent(StreamEvent.ErrorEvent(ParseErrorMessage(err, response.StatusCode.ToString())));
                return;
            }

            // Track tool call accumulation for streaming
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
                        // Finalize accumulated tool calls
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

                    // Use 'as JObject' so JSON null becomes null reference (avoids JValue child-access exception)
                    var delta = choices[0]["delta"] as JObject;
                    if (delta == null) continue;

                    // Text content
                    var textContent = delta["content"]?.ToString();
                    if (!string.IsNullOrEmpty(textContent))
                        onEvent(StreamEvent.TextDeltaEvent(textContent));

                    // Tool calls (streamed incrementally)
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

                    // Usage — OpenAI sends "usage": null on intermediate chunks when include_usage=true.
                    // Use 'as JObject' so JSON null becomes null reference safely.
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

        public virtual async Task<List<ModelInfo>> GetAvailableModelsAsync()
        {
            // Static fallback — also used when no API key configured
            var fallback = new List<ModelInfo>
            {
                new ModelInfo { Id = "gpt-4o",      DisplayName = "GPT-4o",      Provider = ProviderName, MaxTokens = 16384, SupportsTools = true, SupportsVision = true },
                new ModelInfo { Id = "gpt-4o-mini", DisplayName = "GPT-4o Mini", Provider = ProviderName, MaxTokens = 16384, SupportsTools = true },
                new ModelInfo { Id = "gpt-4-turbo", DisplayName = "GPT-4 Turbo", Provider = ProviderName, MaxTokens = 4096,  SupportsTools = true },
                new ModelInfo { Id = "o3-mini",     DisplayName = "o3-mini",     Provider = ProviderName, MaxTokens = 65536, SupportsTools = true,  SupportsThinking = true },
                new ModelInfo { Id = "o1",          DisplayName = "o1",          Provider = ProviderName, MaxTokens = 32768, SupportsTools = false, SupportsThinking = true },
                new ModelInfo { Id = "o1-mini",     DisplayName = "o1-mini",     Provider = ProviderName, MaxTokens = 65536, SupportsTools = false, SupportsThinking = true },
            };

            if (string.IsNullOrEmpty(ApiKey)) return fallback;

            try
            {
                var client = CreateClient();
                var resp = await client.GetAsync($"{BaseUrl}/models");
                if (!resp.IsSuccessStatusCode) return fallback;

                var json = await resp.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                var data = obj["data"] as JArray;
                if (data == null || data.Count == 0) return fallback;

                var chatModels = new List<ModelInfo>();
                foreach (var m in data)
                {
                    var id = m["id"]?.ToString();
                    if (string.IsNullOrEmpty(id)) continue;

                    // Include only chat/completion models; skip embeddings, tts, whisper, dall-e
                    if (!IsChatModel(id)) continue;

                    chatModels.Add(new ModelInfo
                    {
                        Id              = id,
                        DisplayName     = id,
                        Provider        = ProviderName,
                        MaxTokens       = GetDefaultMaxTokens(id),
                        SupportsTools   = SupportsToolCalls(id),
                        SupportsVision  = id.Contains("vision") || id.Contains("4o") || id.Contains("gpt-4-turbo"),
                        SupportsThinking = IsThinkingModel(id)
                    });
                }

                if (chatModels.Count == 0) return fallback;

                // Sort: newer / higher capability models first
                chatModels.Sort((a, b) => string.Compare(b.Id, a.Id, StringComparison.OrdinalIgnoreCase));
                return chatModels;
            }
            catch
            {
                return fallback;
            }
        }

        private string ParseErrorMessage(string rawJson, string statusCode)
        {
            try
            {
                var obj = JObject.Parse(rawJson);
                var msg = obj["error"]?["message"]?.ToString();
                if (!string.IsNullOrEmpty(msg))
                {
                    // Non-chat model: friendly hint
                    if (msg.Contains("not a chat model") || msg.Contains("v1/chat/completions") || msg.Contains("v1/completions"))
                        return $"模型不支援 Chat Completions API。\n請改用支援對話的模型（如 gpt-4o、gpt-4o-mini）。\n\n錯誤詳情：{msg}";

                    // Context length exceeded
                    if (msg.Contains("context_length_exceeded") || msg.Contains("maximum context length"))
                        return $"訊息超過模型的上下文長度限制。請使用 /compact 壓縮對話記錄。\n\n錯誤詳情：{msg}";

                    return $"{ProviderName} 錯誤 ({statusCode}): {msg}";
                }
            }
            catch { }
            return $"{ProviderName} error {statusCode}: {rawJson}";
        }

        private static bool IsChatModel(string id)
        {
            // Exclude non-chat models: embeddings, audio, image generation, legacy completions
            if (id.Contains("embedding"))   return false;
            if (id.Contains("whisper"))     return false;
            if (id.Contains("dall-e"))      return false;
            if (id.Contains("tts"))         return false;
            if (id.Contains("transcribe"))  return false;
            if (id.Contains("search"))      return false;
            if (id.Contains("similarity"))  return false;
            if (id.Contains("instruct"))    return false; // e.g. gpt-3.5-turbo-instruct uses /v1/completions
            // Legacy base models (non-chat): babbage, davinci, curie, ada, text-*
            if (id.StartsWith("babbage"))   return false;
            if (id.StartsWith("davinci"))   return false;
            if (id.StartsWith("curie"))     return false;
            if (id.StartsWith("ada"))       return false;
            if (id.StartsWith("text-"))     return false;
            if (id.StartsWith("code-"))     return false;

            // Keep known chat/reasoning model families
            if (id.StartsWith("gpt-"))
            {
                // gpt-5.x series uses Responses API (/v1/responses), not chat completions
                // Pattern: gpt-5.X-name where X is a digit
                if (System.Text.RegularExpressions.Regex.IsMatch(id, @"^gpt-5\.\d"))
                    return false;
                return true;
            }
            if (id.StartsWith("o1"))        return true;
            if (id.StartsWith("o3"))        return true;
            if (id.StartsWith("o4"))        return true;
            if (id.StartsWith("chatgpt-"))  return true;
            return false;
        }

        private static int GetDefaultMaxTokens(string id)
        {
            if (id.StartsWith("o1") || id.StartsWith("o3") || id.StartsWith("o4")) return 65536;
            if (id.Contains("gpt-4o"))    return 16384;
            if (id.Contains("gpt-4"))     return 8192;
            if (id.Contains("gpt-3.5"))   return 4096;
            return 8192;
        }

        private static bool SupportsToolCalls(string id)
        {
            // o1 (original) did not support tools; o1-mini, o3-mini, o3, o4 do
            if (id == "o1" || id == "o1-preview") return false;
            return true;
        }

        /// <summary>
        /// OpenAI newer models (o1/o3/gpt-4o etc.) use 'max_completion_tokens'.
        /// Ollama and some older-compat endpoints still use 'max_tokens'.
        /// Override to false in subclasses that use the older parameter.
        /// </summary>
        protected virtual bool UseMaxCompletionTokens => true;

        protected virtual HttpClient CreateClient()
        {
            var client = Utils.HttpClientFactory.Create(_config.TimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            return client;
        }

        protected virtual JObject BuildRequestBody(List<ChatMessage> messages, string systemPrompt, ModelParameters parameters, List<ToolDefinition> tools, bool stream)
        {
            var model = parameters?.Model ?? DefaultModel;
            var maxTokens = parameters?.MaxTokens ?? _config.MaxTokens;
            var tokenKey = UseMaxCompletionTokens ? "max_completion_tokens" : "max_tokens";

            var body = new JObject
            {
                ["model"] = model,
                [tokenKey] = maxTokens,
                ["stream"] = stream
            };

            // o1/o3 reasoning models don't support temperature (only default=1 is allowed)
            if (!IsReasoningModel(model))
            {
                body["temperature"] = parameters?.Temperature ?? _config.Temperature;
            }

            if (stream)
            {
                body["stream_options"] = new JObject { ["include_usage"] = true };
            }

            var msgs = _fcAdapter.BuildOpenAIMessages(messages, systemPrompt);
            body["messages"] = msgs;

            if (tools != null && tools.Count > 0)
            {
                body["tools"] = _fcAdapter.ToOpenAITools(tools);
                body["tool_choice"] = "auto";
            }

            return body;
        }

        protected static bool IsReasoningModel(string model)
        {
            if (string.IsNullOrEmpty(model)) return false;
            var m = model.ToLower();
            return m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4")
                || m.StartsWith("gpt-5");
        }

        protected static bool IsThinkingModel(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var m = id.ToLower();
            return m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4");
        }

        protected ModelResponse ParseOpenAIResponse(JObject obj)
        {
            var response = new ModelResponse();

            var usageObj = obj["usage"] as JObject;
            if (usageObj != null)
            {
                response.Usage = new TokenUsage
                {
                    InputTokens = usageObj["prompt_tokens"]?.Value<int>() ?? 0,
                    OutputTokens = usageObj["completion_tokens"]?.Value<int>() ?? 0
                };
            }

            var choices = obj["choices"] as JArray;
            if (choices != null && choices.Count > 0)
            {
                var choice = choices[0] as JObject;
                if (choice != null)
                {
                    response.StopReason = choice["finish_reason"]?.ToString();
                    var message = choice["message"] as JObject;
                    response.Content = message?["content"]?.ToString() ?? "";

                    var toolCallsArr = message?["tool_calls"] as JArray;
                    if (toolCallsArr != null)
                    {
                        response.ToolCalls = _fcAdapter.ParseOpenAIToolCalls(toolCallsArr);
                    }
                }
            }

            return response;
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
    }
}
