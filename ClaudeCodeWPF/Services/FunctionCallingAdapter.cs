using System;
using System.Collections.Generic;
using System.Linq;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>
    /// 統一 Tool Definition 和各供應商格式的轉換適配器
    /// </summary>
    public class FunctionCallingAdapter
    {
        // =================== Tool Definition Converters ===================

        public JArray ToAnthropicTools(List<ToolDefinition> tools)
        {
            var arr = new JArray();
            foreach (var t in tools)
            {
                arr.Add(new JObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = t.InputSchema
                });
            }
            return arr;
        }

        public JArray ToOpenAITools(List<ToolDefinition> tools)
        {
            var arr = new JArray();
            foreach (var t in tools)
            {
                arr.Add(new JObject
                {
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.InputSchema
                    }
                });
            }
            return arr;
        }

        /// <summary>
        /// Azure Responses API (GPT-5.x) uses flat tool format with name/description at top level
        /// </summary>
        public JArray ToResponsesApiTools(List<ToolDefinition> tools)
        {
            var arr = new JArray();
            foreach (var t in tools)
            {
                arr.Add(new JObject
                {
                    ["type"] = "function",
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.InputSchema
                });
            }
            return arr;
        }

        public JArray ToGeminiTools(List<ToolDefinition> tools)
        {
            var declarations = new JArray();
            foreach (var t in tools)
            {
                declarations.Add(new JObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = ConvertSchemaForGemini(t.InputSchema)
                });
            }
            return new JArray
            {
                new JObject { ["function_declarations"] = declarations }
            };
        }

        // Gemini uses slightly different schema format (no $schema, type uses uppercase)
        private JObject ConvertSchemaForGemini(JObject schema)
        {
            if (schema == null) return new JObject { ["type"] = "OBJECT", ["properties"] = new JObject() };
            var clone = (JObject)schema.DeepClone();
            clone.Remove("$schema");
            // Gemini type names are UPPERCASE
            if (clone["type"] != null)
            {
                var t = clone["type"].ToString().ToUpperInvariant();
                if (t == "OBJECT" || t == "STRING" || t == "NUMBER" || t == "INTEGER" || t == "BOOLEAN" || t == "ARRAY")
                    clone["type"] = t;
            }
            return clone;
        }

        // =================== Tool Call Response Parsers ===================

        public List<ToolCall> ParseOpenAIToolCalls(JArray toolCallsArr)
        {
            var result = new List<ToolCall>();
            if (toolCallsArr == null) return result;

            foreach (var tc in toolCallsArr)
            {
                JObject args;
                try { args = JObject.Parse(tc["function"]?["arguments"]?.ToString() ?? "{}"); }
                catch { args = new JObject(); }

                result.Add(new ToolCall
                {
                    Id = tc["id"]?.ToString(),
                    Name = tc["function"]?["name"]?.ToString(),
                    Arguments = args
                });
            }
            return result;
        }

        // =================== Message Format Converters ===================

        /// <summary>Build Anthropic-format messages array (excluding system)</summary>
        public JArray BuildAnthropicMessages(List<ChatMessage> messages)
        {
            var arr = new JArray();
            foreach (var msg in messages)
            {
                if (msg.Role == "system") continue; // system goes in "system" param

                if (msg.Role == "tool")
                {
                    // Tool result: user role with tool_result content block
                    if (msg.ImageBlocks != null && msg.ImageBlocks.Count > 0)
                    {
                        // Multimodal tool result: include text + images in content array
                        var toolResultContent = new JArray();
                        toolResultContent.Add(new JObject { ["type"] = "text", ["text"] = msg.Content ?? "" });
                        foreach (var img in msg.ImageBlocks)
                        {
                            toolResultContent.Add(new JObject
                            {
                                ["type"] = "image",
                                ["source"] = new JObject
                                {
                                    ["type"] = "base64",
                                    ["media_type"] = img.MimeType ?? "image/png",
                                    ["data"] = img.Base64Data ?? ""
                                }
                            });
                        }
                        arr.Add(new JObject
                        {
                            ["role"] = "user",
                            ["content"] = new JArray
                            {
                                new JObject
                                {
                                    ["type"] = "tool_result",
                                    ["tool_use_id"] = msg.ToolCallId,
                                    ["content"] = toolResultContent
                                }
                            }
                        });
                    }
                    else
                    {
                        arr.Add(new JObject
                        {
                            ["role"] = "user",
                            ["content"] = new JArray
                            {
                                new JObject
                                {
                                    ["type"] = "tool_result",
                                    ["tool_use_id"] = msg.ToolCallId,
                                    ["content"] = msg.Content ?? ""
                                }
                            }
                        });
                    }
                }
                else if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    var content = new JArray();
                    if (!string.IsNullOrEmpty(msg.Content))
                        content.Add(new JObject { ["type"] = "text", ["text"] = msg.Content });

                    foreach (var tc in msg.ToolCalls)
                    {
                        content.Add(new JObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = tc.Id,
                            ["name"] = tc.Name,
                            ["input"] = tc.Arguments ?? new JObject()
                        });
                    }

                    arr.Add(new JObject { ["role"] = "assistant", ["content"] = content });
                }
                else
                {
                    arr.Add(new JObject
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content ?? ""
                    });
                }
            }
            return arr;
        }

        /// <summary>Build OpenAI-format messages array (including system)</summary>
        public JArray BuildOpenAIMessages(List<ChatMessage> messages, string systemPrompt)
        {
            var arr = new JArray();

            if (!string.IsNullOrEmpty(systemPrompt))
                arr.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });

            foreach (var msg in messages)
            {
                if (msg.Role == "system") continue;

                if (msg.Role == "tool")
                {
                    arr.Add(new JObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = msg.ToolCallId,
                        ["content"] = msg.Content ?? ""
                    });
                    // OpenAI tool messages cannot contain images; add a follow-up user message
                    if (msg.ImageBlocks != null && msg.ImageBlocks.Count > 0)
                    {
                        var imageContent = new JArray();
                        imageContent.Add(new JObject { ["type"] = "text", ["text"] = "Document pages (from ReadDocument tool result):" });
                        foreach (var img in msg.ImageBlocks)
                        {
                            imageContent.Add(new JObject
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new JObject
                                {
                                    ["url"] = $"data:{img.MimeType ?? "image/png"};base64,{img.Base64Data ?? ""}"
                                }
                            });
                        }
                        arr.Add(new JObject { ["role"] = "user", ["content"] = imageContent });
                    }
                }
                else if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    var toolCalls = new JArray();
                    foreach (var tc in msg.ToolCalls)
                    {
                        toolCalls.Add(new JObject
                        {
                            ["id"] = tc.Id,
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = tc.Name,
                                ["arguments"] = tc.Arguments?.ToString() ?? "{}"
                            }
                        });
                    }

                    var assistantMsg = new JObject { ["role"] = "assistant" };
                    if (!string.IsNullOrEmpty(msg.Content))
                        assistantMsg["content"] = msg.Content;
                    assistantMsg["tool_calls"] = toolCalls;
                    arr.Add(assistantMsg);
                }
                else
                {
                    arr.Add(new JObject
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content ?? ""
                    });
                }
            }
            return arr;
        }

        /// <summary>
        /// Build messages for Azure Responses API (GPT-5.x).
        /// Uses native format: function_call / function_call_output instead of tool_calls / role:tool
        /// </summary>
        public JArray BuildResponsesApiMessages(List<ChatMessage> messages, string systemPrompt)
        {
            var arr = new JArray();

            if (!string.IsNullOrEmpty(systemPrompt))
                arr.Add(new JObject { ["role"] = "system", ["content"] = systemPrompt });

            foreach (var msg in messages)
            {
                if (msg.Role == "system") continue;

                if (msg.Role == "tool")
                {
                    // Responses API: function_call_output
                    arr.Add(new JObject
                    {
                        ["type"] = "function_call_output",
                        ["call_id"] = msg.ToolCallId ?? "",
                        ["output"] = msg.Content ?? ""
                    });
                }
                else if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    // If assistant has text content, emit that as a message first
                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        arr.Add(new JObject { ["role"] = "assistant", ["content"] = msg.Content });
                    }

                    // Responses API: each tool call becomes a function_call item
                    foreach (var tc in msg.ToolCalls)
                    {
                        arr.Add(new JObject
                        {
                            ["type"] = "function_call",
                            ["call_id"] = tc.Id ?? Guid.NewGuid().ToString(),
                            ["name"] = tc.Name ?? "",
                            ["arguments"] = tc.Arguments?.ToString() ?? "{}"
                        });
                    }
                }
                else
                {
                    arr.Add(new JObject
                    {
                        ["role"] = msg.Role,
                        ["content"] = msg.Content ?? ""
                    });
                }
            }
            return arr;
        }

        /// <summary>Build Gemini-format contents array</summary>
        public JArray BuildGeminiContents(List<ChatMessage> messages)
        {
            var arr = new JArray();

            foreach (var msg in messages)
            {
                if (msg.Role == "system") continue;

                string role = msg.Role == "assistant" ? "model" : "user";

                if (msg.Role == "tool")
                {
                    arr.Add(new JObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JArray
                        {
                            new JObject
                            {
                                ["functionResponse"] = new JObject
                                {
                                    ["name"] = msg.Name ?? "tool",
                                    ["response"] = new JObject { ["result"] = msg.Content ?? "" }
                                }
                            }
                        }
                    });
                    // Add images as a follow-up user message
                    if (msg.ImageBlocks != null && msg.ImageBlocks.Count > 0)
                    {
                        var parts = new JArray();
                        parts.Add(new JObject { ["text"] = "Document pages from ReadDocument:" });
                        foreach (var img in msg.ImageBlocks)
                        {
                            parts.Add(new JObject
                            {
                                ["inlineData"] = new JObject
                                {
                                    ["mimeType"] = img.MimeType ?? "image/png",
                                    ["data"] = img.Base64Data ?? ""
                                }
                            });
                        }
                        arr.Add(new JObject { ["role"] = "user", ["parts"] = parts });
                    }
                }
                else if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                {
                    var parts = new JArray();
                    if (!string.IsNullOrEmpty(msg.Content))
                        parts.Add(new JObject { ["text"] = msg.Content });

                    foreach (var tc in msg.ToolCalls)
                    {
                        parts.Add(new JObject
                        {
                            ["functionCall"] = new JObject
                            {
                                ["name"] = tc.Name,
                                ["args"] = tc.Arguments ?? new JObject()
                            }
                        });
                    }

                    arr.Add(new JObject { ["role"] = "model", ["parts"] = parts });
                }
                else
                {
                    arr.Add(new JObject
                    {
                        ["role"] = role,
                        ["parts"] = new JArray { new JObject { ["text"] = msg.Content ?? "" } }
                    });
                }
            }

            return arr;
        }
    }
}
