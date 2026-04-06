# Copilot Instructions — Claude Code WPF

## Build

- **IDE**: Visual Studio 2019 or 2022 with the ".NET Desktop Development" workload
- **Target**: .NET Framework 4.7.2, C# 7.3, WinExe
- **Solution**: `ClaudeCodeWPF.sln`
- **Build**: Open solution → `F5` (run) or `Ctrl+Shift+B` (build only)
- **NuGet**: Only one package — `Newtonsoft.Json 13.0.3`. Restore runs automatically; manual: `nuget restore ClaudeCodeWPF.sln`
- **MSBuild CLI**: `msbuild ClaudeCodeWPF.sln /p:Configuration=Release`
- **No test project exists.**

## Architecture

### Data flow (one user turn)

```
User types → ChatPanel.TrySend()
  → ChatPanel.OnSendMessage (event)
    → MainWindow.OnSendMessage()
      → ConversationManager.GetOrCreateActiveSession()
      → ChatService.SendMessageAsync(session, message, CancellationToken)
        → IModelProvider.SendMessageStream/Async()   ← HTTP call
          → Action<StreamEvent> callback
            → ChatService.OnEvent (event)
              → MainWindow.Dispatcher.InvokeAsync(HandleStreamEvent)
                → ChatPanel.AppendAssistantText / FinalizeAssistantMessage / etc.
```

### Provider layer

`Services/Providers/` contains 8 implementations of `IModelProvider`:

| Class | Protocol |
|---|---|
| `AnthropicProvider` | Anthropic Messages API (SSE) |
| `OpenAIProvider` | OpenAI Chat Completions (SSE) — **base class** |
| `AzureOpenAIProvider` | extends `OpenAIProvider` (different URL/auth) |
| `OllamaProvider` | extends `OpenAIProvider` (local, `max_tokens` not `max_completion_tokens`) |
| `GeminiProvider` | Google Generative Language API (SSE) |
| `BedrockProvider` | AWS Bedrock (stubs to non-streaming) |
| `VertexProvider` | Google Vertex AI (stubs to non-streaming) |
| `FoundryProvider` | Azure AI Foundry (stubs to non-streaming) |

`ModelProviderFactory.Instance.GetCurrentProvider()` returns the currently selected provider at runtime.

`FunctionCallingAdapter` converts `List<ChatMessage>` ↔ each provider's JSON format (Anthropic, OpenAI, Gemini). Always go through this adapter — never build message arrays by hand.

### Tool system

`ToolRegistry` (singleton) holds all tool definitions. `ToolOrchestrator` runs them after the model emits `ToolCallComplete` events. Tools live in `Services/ToolSystem/Tools/`:
`BashTool`, `FileReadTool`, `FileWriteTool`, `FileEditTool`, `GrepTool`, `GlobTool`, `WebFetchTool`, `WebSearchTool`, `AgentTool`

`AgentTool` spawns a child `ChatService` for sub-agent calls.

### Key singletons

| Singleton | Purpose |
|---|---|
| `ConfigService.Instance` | App.config wrapper — all API keys, URLs, defaults |
| `ModelProviderFactory.Instance` | Creates/caches provider instances |
| `ToolRegistry.Instance` | All `IToolExecutor` registrations |
| `ConversationManager.Instance` | Session CRUD + active session |
| `HistoryService.Instance` | Conversation history entries |
| `SystemPromptService.Instance` | System prompt (zh-TW / en) |

### UI panels

`MainWindow.xaml` hosts four panels via `Grid` columns:
- `HistoryPanel` — conversation list
- `ChatPanel` — messages + input box
- `ToolOutputPanel` — tool execution log
- `SettingsPanel` — provider/model selector

All stream event handling in `MainWindow.HandleStreamEvent()` must route UI calls through `Dispatcher.InvokeAsync()`.

## Key Conventions

### C# 7.3 constraints (no newer syntax)
- No `IAsyncEnumerable<T>` — streaming uses `Action<StreamEvent>` callbacks instead
- No switch expressions (`switch { ... => }`) — use `switch` statements
- No null-coalescing assignment (`??=`) — use `_x = _x ?? value`
- No default interface implementations
- `ProcessStartInfo.StandardInputEncoding` does not exist in .NET 4.7.2 — omit it

### Safe Newtonsoft.Json access
Always use `as JObject` / `as JArray` when accessing child nodes — never assume a `JToken` indexer won't hit a `JValue`:
```csharp
// CORRECT — JSON null becomes C# null safely
var usageObj = evt["usage"] as JObject;
if (usageObj != null) { ... }

// WRONG — throws "Cannot access child value on JValue" if value is JSON null
var usageObj = evt["usage"];
if (usageObj != null) { usageObj["prompt_tokens"]... }  // boom
```
This is the root cause of the most common streaming parse errors in this codebase.

### OpenAI API quirks
- Use `max_completion_tokens` (not `max_tokens`) — controlled by `OpenAIProvider.UseMaxCompletionTokens` (override to `false` in `OllamaProvider`)
- Omit `temperature` entirely for reasoning models (`o1`, `o3`, `o4` prefix) — use `IsReasoningModel(model)` check
- With `stream_options: { include_usage: true }`, intermediate chunks send `"usage": null` (a `JValue`, not absent) — the `as JObject` pattern above is the fix

### StreamEvent pattern
Providers signal state via `StreamEvent` values:
- API errors → `StreamEvent.ErrorEvent(msg)` — **do not throw** from the streaming loop
- Text → `StreamEvent.TextDeltaEvent(text)`
- Tool call complete (fully built) → `StreamEvent.ToolCallComplete` with `ToolCall.Arguments` as parsed `JObject`
- `ChatService` only consumes `ToolCallComplete` (not `ToolCallStart`/`ToolCallDelta`) for accumulation

### Configuration
All API keys, base URLs, and defaults live in `ClaudeCodeWPF/App.config` under `<appSettings>`. Key naming: `Provider.PropertyName` (e.g., `OpenAI.ApiKey`, `Gemini.BaseUrl`).

Runtime provider/model selection is stored in `ConfigService.CurrentProvider` and `ConfigService.CurrentModel` (in-memory, not persisted). Changing `CurrentProvider` resets `_currentModel` to null.

### WPF UI patterns
- Use `DispatcherTimer` (not `System.Timers.Timer`) for UI animations
- `PreviewKeyDown` (not `KeyDown`) to intercept Enter before `AcceptsReturn="True"` inserts a newline
- `ChatPanel` builds all message UI elements in code-behind (no XAML templates for messages)
- Lazy container creation: `EnsureAssistantContainer()` is called on first content, not at `StartAssistantMessage()`
- Do **not** manually add `Microsoft.WinFX.targets` to the `.csproj` — it is auto-imported and causes a duplicate warning

### Adding a new provider
1. Implement `IModelProvider` (or extend `OpenAIProvider` for OpenAI-compatible APIs)
2. Register in `ModelProviderFactory.cs`
3. Add config keys to `App.config` following the `Provider.*` naming convention
4. Add provider-specific defaults to `ConfigService.GetCurrentDefaultModel()`
5. Add the model list to the `ProviderComboBox` data in `MainWindow.xaml`
