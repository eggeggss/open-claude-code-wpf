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
    /// Google Gemini API 供應商
    /// POST https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?key=...
    /// </summary>
    public class GeminiProvider : IModelProvider
    {
        private readonly ConfigService _config;
        private readonly FunctionCallingAdapter _fcAdapter;

        public string ProviderName => "Gemini";
        public bool IsAvailable => !string.IsNullOrEmpty(_config.GetApiKeyForProvider("Gemini"));

        public GeminiProvider()
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
            var model = parameters?.Model ?? _config.GeminiDefaultModel;
            var body = BuildGeminiBody(messages, systemPrompt, parameters, tools);
            var json = JsonConvert.SerializeObject(body);

            // Try v1beta first, fall back to v1alpha on 404
            foreach (var apiVer in new[] { "v1beta", "v1alpha" })
            {
                var url = $"{_config.GetBaseUrlForProvider("Gemini")}/{apiVer}/models/{model}:generateContent?key={_config.GetApiKeyForProvider("Gemini")}";
                for (int retry = 0; retry <= _config.MaxRetries; retry++)
                {
                    try
                    {
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await client.PostAsync(url, content, cancellationToken);
                        var responseText = await response.Content.ReadAsStringAsync();

                        if (!response.IsSuccessStatusCode)
                        {
                            // 404 = try next api version
                            if ((int)response.StatusCode == 404) break;
                            if ((int)response.StatusCode == 429 && retry < _config.MaxRetries)
                            {
                                await Task.Delay(1000 * (int)Math.Pow(2, retry), cancellationToken);
                                continue;
                            }
                            throw new Exception($"Gemini API error {response.StatusCode}: {responseText}");
                        }

                        return ParseGeminiResponse(JObject.Parse(responseText));
                    }
                    catch (TaskCanceledException) { throw; }
                    catch (Exception) when (retry < _config.MaxRetries)
                    {
                        await Task.Delay(1000 * (int)Math.Pow(2, retry), cancellationToken);
                    }
                }
            }
            throw new Exception($"Gemini: model '{model}' not found on v1beta or v1alpha");
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
            var model = parameters?.Model ?? _config.GeminiDefaultModel;
            var body = BuildGeminiBody(messages, systemPrompt, parameters, tools);
            var json = JsonConvert.SerializeObject(body);

            // Try v1beta first, fall back to v1alpha on 404
            foreach (var apiVer in new[] { "v1beta", "v1alpha" })
            {
                var url = $"{_config.GetBaseUrlForProvider("Gemini")}/{apiVer}/models/{model}:streamGenerateContent?key={_config.GetApiKeyForProvider("Gemini")}&alt=sse";
                var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = requestContent };
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    // 404 → try next api version silently
                    if ((int)response.StatusCode == 404) continue;
                    onEvent(StreamEvent.ErrorEvent($"Gemini error {response.StatusCode}: {err}"));
                    return;
                }

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (!line.StartsWith("data: ")) continue;

                        var data = line.Substring(6).Trim();
                        if (data == "[DONE]") { onEvent(StreamEvent.MessageEndEvent("stop")); return; }

                        JObject evt;
                        try { evt = JObject.Parse(data); }
                        catch { continue; }

                        var candidates = evt["candidates"] as JArray;
                        if (candidates == null || candidates.Count == 0) continue;

                        var candidate = candidates[0];
                        var contentObj = candidate["content"];
                        var parts = contentObj?["parts"] as JArray;
                        if (parts == null) continue;

                        foreach (var part in parts)
                        {
                            var textPart = part["text"]?.ToString();
                            if (!string.IsNullOrEmpty(textPart))
                                onEvent(StreamEvent.TextDeltaEvent(textPart));

                            var funcCall = part["functionCall"];
                            if (funcCall != null)
                            {
                                var name = funcCall["name"]?.ToString();
                                var args = funcCall["args"] as JObject ?? new JObject();
                                var tc = new ToolCall { Id = Guid.NewGuid().ToString(), Name = name, Arguments = args };
                                onEvent(new StreamEvent { Type = StreamEventType.ToolCallStart, ToolCall = tc });
                                onEvent(new StreamEvent { Type = StreamEventType.ToolCallComplete, ToolCall = tc });
                            }
                        }

                        var finishReason = candidate["finishReason"]?.ToString();
                        if (finishReason == "STOP" || finishReason == "MAX_TOKENS")
                        {
                            var usageMeta = evt["usageMetadata"];
                            TokenUsage usage = null;
                            if (usageMeta != null)
                                usage = new TokenUsage
                                {
                                    InputTokens  = usageMeta["promptTokenCount"]?.Value<int>() ?? 0,
                                    OutputTokens = usageMeta["candidatesTokenCount"]?.Value<int>() ?? 0
                                };
                            onEvent(StreamEvent.MessageEndEvent(finishReason, usage));
                            return;
                        }
                    }
                }
                return; // stream finished successfully
            }
            onEvent(StreamEvent.ErrorEvent($"Gemini: model '{model}' not found on v1beta or v1alpha"));
        }

        public async Task<List<ModelInfo>> GetAvailableModelsAsync()
        {
            // Try to fetch live model list from the Gemini API
            if (!string.IsNullOrEmpty(_config.GetApiKeyForProvider("Gemini")))
            {
                try
                {
                    var client = Utils.HttpClientFactory.Create(15);
                    var url = $"{_config.GetBaseUrlForProvider("Gemini")}/v1beta/models?key={_config.GetApiKeyForProvider("Gemini")}";
                    var resp = await client.GetAsync(url);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        var obj = JObject.Parse(json);
                        var models = obj["models"] as JArray;
                        if (models != null && models.Count > 0)
                        {
                            var result = new List<ModelInfo>();
                            foreach (var m in models)
                            {
                                var methods = m["supportedGenerationMethods"] as JArray;
                                if (methods == null) continue;
                                bool supportsGenerate = false;
                                foreach (var method in methods)
                                    if (method.ToString() == "generateContent") { supportsGenerate = true; break; }
                                if (!supportsGenerate) continue;

                                // "models/gemini-1.5-flash" → "gemini-1.5-flash"
                                var fullName = m["name"]?.ToString() ?? "";
                                var id = fullName.StartsWith("models/") ? fullName.Substring(7) : fullName;
                                if (string.IsNullOrEmpty(id)) continue;

                                var display = m["displayName"]?.ToString() ?? id;
                                var tokens = m["outputTokenLimit"]?.Value<int>() ?? 8192;
                                result.Add(new ModelInfo
                                {
                                    Id = id,
                                    DisplayName = display,
                                    Provider = ProviderName,
                                    MaxTokens = tokens,
                                    SupportsTools = true,
                                    SupportsVision = id.Contains("gemini"),
                                    SupportsThinking = id.Contains("thinking") || id.Contains("flash-exp") || id.Contains("2.0-flash") || id.Contains("2.5")
                                });
                            }
                            if (result.Count > 0)
                            {
                                // Sort: newer / larger models first
                                result.Sort((a, b) => string.Compare(b.Id, a.Id, StringComparison.Ordinal));
                                return result;
                            }
                        }
                    }
                }
                catch { /* fall through to static list */ }
            }

            // Fallback static list (stable models known to work on v1beta)
            return new List<ModelInfo>
            {
                new ModelInfo { Id = "gemini-2.0-flash-lite",  DisplayName = "Gemini 2.0 Flash Lite",  Provider = ProviderName, MaxTokens = 8192,  SupportsTools = true, SupportsVision = true },
                new ModelInfo { Id = "gemini-1.5-pro-latest",  DisplayName = "Gemini 1.5 Pro Latest",  Provider = ProviderName, MaxTokens = 8192,  SupportsTools = true, SupportsVision = true },
                new ModelInfo { Id = "gemini-1.5-flash-latest",DisplayName = "Gemini 1.5 Flash Latest",Provider = ProviderName, MaxTokens = 8192,  SupportsTools = true },
                new ModelInfo { Id = "gemini-1.5-pro",         DisplayName = "Gemini 1.5 Pro",         Provider = ProviderName, MaxTokens = 8192,  SupportsTools = true, SupportsVision = true },
                new ModelInfo { Id = "gemini-1.5-flash",       DisplayName = "Gemini 1.5 Flash",       Provider = ProviderName, MaxTokens = 8192,  SupportsTools = true },
            };
        }

        /// <summary>
        /// Determines the Gemini API version for a model.
        /// Most models use v1beta; experimental/preview 2.5 models may need v1alpha.
        /// Always tries v1beta first since Google keeps adding newer models there.
        /// </summary>
        private static string GetApiVersion(string model)
        {
            // v1alpha is only needed for very early experimental models
            // gemini-2.5-pro-exp-* style — but since Google keeps updating endpoints,
            // default to v1beta and let the caller handle any 404 with v1alpha retry.
            return "v1beta";
        }

        private JObject BuildGeminiBody(List<ChatMessage> messages, string systemPrompt, ModelParameters parameters, List<ToolDefinition> tools)
        {
            var body = new JObject();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                body["system_instruction"] = new JObject
                {
                    ["parts"] = new JArray { new JObject { ["text"] = systemPrompt } }
                };
            }

            body["contents"] = _fcAdapter.BuildGeminiContents(messages);

            var genConfig = new JObject
            {
                ["maxOutputTokens"] = parameters?.MaxTokens ?? _config.MaxTokens,
                ["temperature"] = parameters?.Temperature ?? _config.Temperature
            };
            body["generationConfig"] = genConfig;

            if (tools != null && tools.Count > 0)
                body["tools"] = _fcAdapter.ToGeminiTools(tools);

            return body;
        }

        private ModelResponse ParseGeminiResponse(JObject obj)
        {
            var response = new ModelResponse();

            var usageMeta = obj["usageMetadata"];
            if (usageMeta != null)
            {
                response.Usage = new TokenUsage
                {
                    InputTokens = usageMeta["promptTokenCount"]?.Value<int>() ?? 0,
                    OutputTokens = usageMeta["candidatesTokenCount"]?.Value<int>() ?? 0
                };
            }

            var candidates = obj["candidates"] as JArray;
            if (candidates != null && candidates.Count > 0)
            {
                var candidate = candidates[0];
                response.StopReason = candidate["finishReason"]?.ToString();

                var parts = candidate["content"]?["parts"] as JArray;
                if (parts != null)
                {
                    var textParts = new StringBuilder();
                    var toolCalls = new List<ToolCall>();

                    foreach (var part in parts)
                    {
                        var text = part["text"]?.ToString();
                        if (!string.IsNullOrEmpty(text)) textParts.Append(text);

                        var fc = part["functionCall"];
                        if (fc != null)
                        {
                            toolCalls.Add(new ToolCall
                            {
                                Id = Guid.NewGuid().ToString(),
                                Name = fc["name"]?.ToString(),
                                Arguments = fc["args"] as JObject ?? new JObject()
                            });
                        }
                    }

                    response.Content = textParts.ToString();
                    if (toolCalls.Count > 0) response.ToolCalls = toolCalls;
                }
            }

            return response;
        }
    }
}
