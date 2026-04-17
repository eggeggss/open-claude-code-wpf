# Open Claude Code WPF

> A Windows desktop AI chat client built with WPF (.NET Framework 4.7.2), supporting multiple AI providers in a single interface.

📖 [繁體中文版](README.md)

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-purple)
![Language](https://img.shields.io/badge/language-C%23%207.3-brightgreen)

---

## 🎬 Demo Video

[![Open Claude Code WPF Demo](https://img.youtube.com/vi/Wp2fpYDPPt0/maxresdefault.jpg)](https://www.youtube.com/watch?v=Wp2fpYDPPt0)

> Click the thumbnail above or [watch on YouTube](https://www.youtube.com/watch?v=Wp2fpYDPPt0)

---

### 🤖 AI Auto Form-Filling Demo

[![AI Leave Request Form Demo](https://img.youtube.com/vi/hnivybYTK_0/maxresdefault.jpg)](https://www.youtube.com/watch?v=hnivybYTK_0)

> Demonstrates an AI Agent automatically operating a browser to complete a leave request workflow. [Watch on YouTube](https://www.youtube.com/watch?v=hnivybYTK_0)

**Prompt used:**
```
Help me submit a leave request: 2026/04/11 08:00 ~ 2026/04/12 09:00, reason for leave is to go
to the bank for errands, leave type is Personal Leave (事假). The approval system is at
https://eggeggss.github.io/fake-bpm-for-test/main.html# — login with account rogerroan,
password xxxx. No need to confirm with me.
```

---

### 📊 AI Auto Slide Generation Demo

[![AI Slide Generation Demo](https://img.youtube.com/vi/dyxVlLfbEPE/maxresdefault.jpg)](https://www.youtube.com/watch?v=dyxVlLfbEPE)

> Demonstrates an AI Agent reading a webpage, summarising key points using the Pyramid Principle, and automatically generating a PPTX presentation. [Watch on YouTube](https://www.youtube.com/watch?v=dyxVlLfbEPE)

**Prompt used:**
```
Summarise the key points from this website:
1. Structure the content using the Pyramid Principle:
   conclusion → explanation of each conclusion → supporting details
2. Website URL: https://ihower.tw/blog/12373-rag-chunking
3. Generate a PowerPoint presentation and save it to C:\Download\skill\ebook\rag_v3.pptx
4. Use Comic Sans MS as the font; make the slide style lively with a white background
5. Use C:\Download\skill\ebook\background.png as the background image
```


## Features

- **Multi-provider support** — Anthropic Claude, OpenAI GPT, Google Gemini, Ollama (local), Azure OpenAI (multi-node), Azure Responses API (GPT-5.x)
- **Streaming responses** — real-time token-by-token output
- **Extended thinking** — displays the model's reasoning process in a collapsible panel (for Claude / o1 / o3 series)
- **Tool / Function Calling** — shows tool invocations inline during generation
- **Conversation history** — sessions persisted locally, searchable sidebar
- **Themes** — Light, Dark, Cloude Code (custom orange-accent dark theme)
- **Markdown rendering** — code blocks with syntax highlighting
- **Customisable font** — family and size applied to both chat messages and the input box
- **Model badges** in the dropdown — **工** (tools), **視** (vision), **T** (thinking)

---

## Requirements

| Requirement | Version |
|---|---|
| Windows | 10 / 11 |
| .NET Framework | 4.7.2 or later |
| Visual Studio | 2019 / 2022 (for building) |

---

## Quick Start

### 1. Clone

```bash
git clone https://github.com/eggeggss/open-claude-code-wpf.git
cd open-claude-code-wpf
```

### 2. Restore NuGet packages

Open `OpenClaudeCodeWPF.sln` in Visual Studio, then:

```
Tools → NuGet Package Manager → Restore Packages
```

Or via CLI:

```bash
nuget restore OpenClaudeCodeWPF.sln
```

### 3. Configure API keys

Copy the example config and fill in your keys:

```bash
cp OpenClaudeCodeWPF/App.config.example OpenClaudeCodeWPF/App.config
```

Then edit `App.config`:

```xml
<!-- Anthropic Claude -->
<add key="Anthropic.ApiKey" value="sk-ant-..." />

<!-- OpenAI -->
<add key="OpenAI.ApiKey" value="sk-..." />

<!-- Google Gemini -->
<add key="Gemini.ApiKey" value="AIza..." />

<!-- Ollama (local, no key needed) -->
<add key="Ollama.BaseUrl" value="http://localhost:11434" />

<!-- Azure OpenAI (multi-node, see below) -->
<add key="AzureOpenAI.ApiKey" value="..." />
<add key="AzureOpenAI.Endpoint" value="https://xxx.openai.azure.com" />
<add key="AzureOpenAI.DeploymentName" value="gpt-4o" />
```

> **Tip:** You can also set / change keys at runtime via **Settings → 供應商 API** — they are saved to `%APPDATA%\OpenClaudeCodeWPF\usersettings.json` and do not require a restart.

### 4. Build & Run

Press **F5** in Visual Studio, or:

```bash
msbuild OpenClaudeCodeWPF.sln /p:Configuration=Release
```

The executable will be at `OpenClaudeCodeWPF\bin\Release\OpenClaudeCodeWPF.exe`.

---

## 📦 Packaging as MSI Installer (WiX)

> Skip this section if you only need to run locally.

### Prerequisites

1. Install **WiX Toolset v3.11+**: [https://wixtoolset.org/releases/](https://wixtoolset.org/releases/)
2. (Optional) Install **WiX Toolset Visual Studio Extension** via VS → Extensions → Manage Extensions for syntax support

### Build the MSI

**Option A: Visual Studio**

1. Build the main project first: right-click `OpenClaudeCodeWPF` → **Build** (Release / Any CPU)
2. Then build the installer: right-click `ClaudeCodeWPF.Installer` → **Build** (Release / x86)
3. Output: `ClaudeCodeWPF.Installer\bin\Release\OpenClaudeCodeWPF-Setup.msi`

**Option B: Command Line**

```bat
rem Build main project first
msbuild ClaudeCodeWPF\ClaudeCodeWPF.csproj /p:Configuration=Release /p:Platform="Any CPU"

rem Then build the installer project
msbuild ClaudeCodeWPF.Installer\ClaudeCodeWPF.Installer.wixproj /p:Configuration=Release /p:Platform=x86
```

### Version Bump Checklist

1. Update `AssemblyVersion` in `ClaudeCodeWPF/Properties/AssemblyInfo.cs`
2. Update `<Product Version="X.Y.Z.0" ...>` in `ClaudeCodeWPF.Installer/Product.wxs`
3. **Do NOT** change the `UpgradeCode` GUID — it uniquely identifies all versions of this product
4. Rebuild the MSI; the old version will be automatically removed before installing the new one

---

## Settings

Open the **設定** button in the toolbar. Settings are organised into four tabs:

| Tab | Contents |
|---|---|
| 🔑 供應商 API | Per-provider API keys, base URLs, Ollama model list, Azure multi-node |
| ⚙️ 模型參數 | Temperature, Max Tokens, streaming toggle, language |
| 🎨 介面 | Theme selector, font family, font size |
| 📝 系統提示 | Custom system prompt |

Settings are auto-saved to `%APPDATA%\OpenClaudeCodeWPF\usersettings.json`.

---

## Azure OpenAI Multi-node

### Azure OpenAI Chat Completions API (GPT-4.x)

In **Settings → 供應商 API → Azure OpenAI**, each line represents one deployment node:

```
Name|Endpoint URL|API Key|Deployment Name|API Version
East US|https://myhub-eastus.openai.azure.com|YOUR_API_KEY_HERE|gpt-4o|2024-12-01-preview
Japan East|https://myhub-japan.openai.azure.com|YOUR_API_KEY_HERE|gpt-4o-mini|2024-12-01-preview
```

After saving, each node appears as a selectable model in the model dropdown.

### Azure Responses API (GPT-5.x / Codex)

In **Settings → 供應商 API → Azure Responses**, each line represents one model node:

```
Name|Endpoint URL|API Key|Model Name|API Version
GPT5-Codex|https://your-instance.openai.azure.com/|YOUR_API_KEY_HERE|gpt-5.1-codex-mini|2025-04-01-preview
GPT5-Mini|https://your-instance.openai.azure.com/|YOUR_API_KEY_HERE|gpt-5-mini|2025-04-01-preview
```

**Important notes:**
- Azure Responses API uses a different endpoint format (`/openai/responses`), incompatible with Chat Completions API
- Authentication uses `Authorization: Bearer {API_KEY}` (not `api-key` header)
- Codex models (e.g. `gpt-5.1-codex-mini`) do not support the `temperature` parameter
- Function calling format is native `function_call` / `function_call_output`, different from Chat Completions' `tool_calls`

---

## Ollama (Local Models)

1. Start Ollama: `ollama serve`
2. Pull a model: `ollama pull llama3`
3. In settings, set **Ollama Base URL** to `http://localhost:11434`
4. Leave **Custom Models** blank to auto-detect, or list model IDs one per line:
   ```
   llama3
   mistral
   codellama
   ```

---

## Model Badges

| Badge | Colour | Meaning |
|---|---|---|
| **工** | Orange | Supports Function Calling (Tool Use) |
| **視** | Blue | Supports Vision / Multimodal input |
| **T** | Green | Supports Extended Thinking (reasoning) |

Hover over the badge legend `工 視 T ?` next to the model dropdown for a quick reminder.

---

## Architecture (MVVM)

This project follows the **MVVM (Model-View-ViewModel)** pattern, cleanly separating UI presentation from business logic.

![MVVM Architecture Diagram](doc/mvvm-architecture.png)

| Layer | Responsibility |
|---|---|
| **Views** | XAML-only presentation; communicate with ViewModels exclusively through DataBinding and Commands |
| **ViewModels** | Extend `ViewModelBase` (`INotifyPropertyChanged`); hold all UI state and `ICommand` implementations; never reference a View directly |
| **Services** | Stateless business logic (API calls, settings persistence, theme management); invoked by ViewModels |

### ViewModel Mapping

| ViewModel | View | Responsibilities |
|---|---|---|
| `MainWindowViewModel` | `MainWindow.xaml` | Status bar, provider/model selection, streaming toggle, context usage indicator |
| `ChatViewModel` | `ChatPanel.xaml` | Input text, Send/Cancel commands, slash-command hint visibility |
| `SettingsViewModel` | `SettingsPanel.xaml` | All settings properties (API keys, font, theme, temperature) — Load & Save |
| `HistoryViewModel` | `HistoryPanel.xaml` | Session list filtering, search, selection notification back to MainWindow |

---

## Agent Processing Model (ReAct)

The agent core follows the **ReAct (Reasoning + Acting)** pattern — the model alternates between reasoning and acting until the task is complete.

![ReAct Agent Loop](doc/react-flow.png)

### Flow

```
User input
  └─ [Reason] LLM generates a response (may include tool_calls)
       └─ [Act]    ToolOrchestrator executes tools, returns Observation
            └─ [Reason] LLM sees results, reasons again
                 └─ [Act]    Execute next batch of tools
                      └─ ... (up to 20 rounds)
                           └─ LLM stops calling tools → outputs final reply
```

### Two ReAct Implementations

| Implementation | Use Case | Max Iterations | Status |
|---|---|---|---|
| **ChatService main loop** (API Function Calling) | Models with native tool-call support: Claude / GPT / Gemini | **20 rounds** | ✅ Active in main flow |
| **ReActEngine** (plain-text format) | Models without Function Calling (older Ollama) | **15 rounds** | ⚠️ Implemented, not yet wired |

ReActEngine communicates with the LLM using the classic text protocol:

```
Thought:       [model's reasoning]
Action:        [tool name]
Action Input:  {"param": "value"}
Observation:   [tool result]
... repeats until ...
Final Answer:  [final response]
```

### Sub-agent Recursion

`AgentTool` lets the main agent spin up a sub-agent to handle a focused sub-task:

```
Main Agent (max 20 rounds)
  └─ calls AgentTool
       └─ spawns new ChatService → RunSingleAsync
            └─ Sub-agent (max 20 rounds, has all tools)
                 └─ may call AgentTool again → Grandchild agent
                      └─ no hard depth limit
```

> **Note:** There is currently no hard recursion depth limit. In practice, the LLM rarely goes beyond 2–3 levels spontaneously. A `depth` guard can be added to `AgentTool` to prevent runaway recursion.

---

## Project Structure

```
OpenClaudeCodeWPF/
├── Models/              # Data models (ChatMessage, ModelInfo, StreamEvent …)
├── Services/
│   ├── Providers/       # One file per AI provider
│   ├── ConfigService.cs # Central settings (App.config + runtime overrides)
│   ├── UserSettingsService.cs  # Persists settings to JSON
│   ├── ThemeService.cs  # Theme management
│   ├── ChatService.cs   # Orchestrates provider calls & streaming
│   └── …
├── ViewModels/          # MVVM ViewModel layer
│   ├── ViewModelBase.cs          # INotifyPropertyChanged base class
│   ├── RelayCommand.cs           # ICommand implementation
│   ├── MainWindowViewModel.cs    # Main window state
│   ├── ChatViewModel.cs          # Chat input state & commands
│   ├── SettingsViewModel.cs      # Settings panel state
│   └── HistoryViewModel.cs       # History panel state
├── Views/
│   ├── ChatPanel.xaml   # Main conversation area
│   ├── SettingsPanel.xaml
│   ├── HistoryPanel.xaml
│   └── …
├── App.xaml             # Global resources & styles
├── MainWindow.xaml      # Shell: toolbar + provider/model selectors
└── App.config           # Default configuration (keys go here or in Settings UI)
```

---

## Privacy & Security

- **`App.config`** is git-ignored — never committed. Copy `App.config.example` to `App.config` and fill in your keys locally.
- You can also set keys at runtime via **Settings → 供應商 API**; they are saved to `%APPDATA%\OpenClaudeCodeWPF\usersettings.json` (also gitignored).
- Conversation history is stored locally in `%APPDATA%\OpenClaudeCodeWPF\`.
- No telemetry is collected.

---

## License

MIT

---

## Changelog

## Changelog

### v0.1.4 (2026-04-17)
- **Added** Azure Responses API support (GPT-5.x / Codex series)
  - Dedicated `AzureResponsesProvider` using `/openai/responses` endpoint
  - Supports new models like `gpt-5.1-codex-mini`, `gpt-5-mini`
  - Automatically handles Codex models' lack of `temperature` parameter support
  - Native `function_call` / `function_call_output` format with function calling support
- **Fixed** Azure Responses API streaming parsing (using `item_id` to match delta events)
- **Fixed** Non-streaming function call parsing (output array top-level `type: "function_call"`)
- **Updated** Settings UI with dedicated Azure Responses configuration section
- **Updated** Configuration save/load logic with persistence support for Azure Responses nodes

### v0.1.3
- Added OpenRouter provider with 348 hardcoded models (GPT / Claude / Gemini / Llama and more)
- Added AI auto form-filling demo video (browser Agent operating a leave request system)
- Updated README (EN/ZH)

### v0.1.2
- Fixed access denied error on startup when installed to Program Files (logs path permission issue)
- Release build now writes logs to `%AppData%\OpenClaudeCodeWPF\logs\` to avoid UAC write restrictions

### v0.1.1
- Replaced Playwright with Chrome DevTools Protocol (CDP) for browser automation — no longer requires node.exe or any installation
- Added `browser_connect` / `browser_fill` / `browser_select` tools
- `browser_connect` now supports `wait_ready` parameter — automatically waits up to 20 seconds for CDP to become ready after launching Edge
- Increased agent tool iteration limit from 20 to 50
- Fixed agent hang issue (IsFinalTurn) where UI would stop responding mid-loop
- Added JSON Lines conversation log (saved to `logs/YYYY-MM-DD.jsonl` next to the exe)

### v0.1.0
- Initial release
