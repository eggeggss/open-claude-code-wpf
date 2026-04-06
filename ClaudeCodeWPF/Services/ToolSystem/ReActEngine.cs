using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services.ToolSystem
{
    /// <summary>
    /// ReAct 推理引擎 — 用於不原生支援 Function Calling 的模型
    /// 格式: Thought → Action → Action Input → Observation 迴圈
    /// </summary>
    public class ReActEngine
    {
        private readonly ToolRegistry _registry;
        private const int MaxIterations = 15;

        private static readonly string REACT_SYSTEM_PROMPT = @"
You have access to the following tools:
{TOOL_DESCRIPTIONS}

To use a tool, you MUST respond in exactly this format:
Thought: [your reasoning about what to do]
Action: [exact tool name from the list]
Action Input: [JSON object with the tool's input parameters]

After the tool runs, you will see:
Observation: [tool output]

Continue using tools until you have enough information, then respond:
Thought: I now have all the information needed.
Final Answer: [your complete response to the user]

IMPORTANT: Always use the exact JSON format for Action Input. Do not add explanation after Action Input.
".Trim();

        public ReActEngine(ToolRegistry registry = null)
        {
            _registry = registry ?? ToolRegistry.Instance;
        }

        public async Task<string> RunAsync(
            IModelProvider provider,
            List<ChatMessage> messages,
            string systemPrompt,
            ModelParameters parameters,
            Action<StreamEvent> onEvent,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var tools = _registry.GetAllTools();
            var toolDesc = BuildToolDescriptions(tools);
            var reactSystemPrompt = REACT_SYSTEM_PROMPT.Replace("{TOOL_DESCRIPTIONS}", toolDesc);

            if (!string.IsNullOrEmpty(systemPrompt))
                reactSystemPrompt = systemPrompt + "\n\n" + reactSystemPrompt;

            var workingMessages = new List<ChatMessage>(messages);

            for (int iteration = 0; iteration < MaxIterations; iteration++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var response = await provider.SendMessageAsync(
                    workingMessages, reactSystemPrompt, parameters,
                    tools: null, cancellationToken: cancellationToken);

                var responseText = response.Content ?? "";

                // Parse ReAct response
                var parsed = ParseReActResponse(responseText);

                if (parsed.FinalAnswer != null)
                {
                    onEvent(StreamEvent.TextDeltaEvent(parsed.FinalAnswer));
                    onEvent(StreamEvent.MessageEndEvent("stop", response.Usage));
                    return parsed.FinalAnswer;
                }

                if (parsed.Action != null && parsed.ActionInput != null)
                {
                    // Show thought to user
                    if (!string.IsNullOrEmpty(parsed.Thought))
                        onEvent(StreamEvent.ThinkingDeltaEvent(parsed.Thought));

                    // Notify tool start
                    var toolCallId = Guid.NewGuid().ToString();
                    var tc = new ToolCall
                    {
                        Id = toolCallId,
                        Name = parsed.Action,
                        Arguments = Newtonsoft.Json.Linq.JObject.Parse(parsed.ActionInput)
                    };
                    onEvent(new StreamEvent { Type = StreamEventType.ToolCallStart, ToolCall = tc });

                    // Execute tool
                    string observation;
                    if (_registry.TryGetTool(parsed.Action, out var tool))
                    {
                        try
                        {
                            var result = await tool.ExecuteAsync(tc.Arguments, cancellationToken);
                            observation = result.IsSuccess ? result.Content : $"Error: {result.Error}";
                        }
                        catch (Exception ex)
                        {
                            observation = $"Error: {ex.Message}";
                        }
                    }
                    else
                    {
                        observation = $"Error: Tool '{parsed.Action}' not found. Available tools: {string.Join(", ", tools.ConvertAll(t => t.Name))}";
                    }

                    tc.Arguments = tc.Arguments ?? new Newtonsoft.Json.Linq.JObject();
                    onEvent(new StreamEvent { Type = StreamEventType.ToolCallComplete, ToolCall = tc });

                    // Add assistant message and observation to working messages
                    workingMessages.Add(ChatMessage.Assistant(responseText));
                    workingMessages.Add(ChatMessage.User($"Observation: {observation}"));
                }
                else
                {
                    // No structured output — just return as-is
                    onEvent(StreamEvent.TextDeltaEvent(responseText));
                    onEvent(StreamEvent.MessageEndEvent("stop", response.Usage));
                    return responseText;
                }
            }

            onEvent(StreamEvent.ErrorEvent("ReAct: max iterations reached"));
            return "Error: Maximum ReAct iterations reached";
        }

        private string BuildToolDescriptions(List<IToolExecutor> tools)
        {
            var sb = new StringBuilder();
            foreach (var t in tools)
            {
                sb.AppendLine($"- **{t.Name}**: {t.Description}");
                sb.AppendLine($"  Input schema: {t.InputSchema}");
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private ReActParsed ParseReActResponse(string text)
        {
            var parsed = new ReActParsed();

            var finalAnswerMatch = Regex.Match(text, @"Final Answer:\s*(.+?)(?:\n|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (finalAnswerMatch.Success)
            {
                parsed.FinalAnswer = finalAnswerMatch.Groups[1].Value.Trim();
                return parsed;
            }

            var thoughtMatch = Regex.Match(text, @"Thought:\s*(.+?)(?=Action:|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (thoughtMatch.Success)
                parsed.Thought = thoughtMatch.Groups[1].Value.Trim();

            var actionMatch = Regex.Match(text, @"Action:\s*(.+?)(?:\n|$)", RegexOptions.IgnoreCase);
            if (actionMatch.Success)
                parsed.Action = actionMatch.Groups[1].Value.Trim();

            var actionInputMatch = Regex.Match(text, @"Action Input:\s*(\{.+?\})", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (actionInputMatch.Success)
                parsed.ActionInput = actionInputMatch.Groups[1].Value.Trim();

            return parsed;
        }

        private class ReActParsed
        {
            public string Thought { get; set; }
            public string Action { get; set; }
            public string ActionInput { get; set; }
            public string FinalAnswer { get; set; }
        }
    }
}
