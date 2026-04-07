# Open Claude Code WPF

> 基於 WPF（.NET Framework 4.7.2）開發的 Windows 桌面 AI 對話用戶端，整合多家 AI 供應商於單一介面。

📖 [English Version](README.en.md)

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-purple)
![Language](https://img.shields.io/badge/language-C%23%207.3-brightgreen)
![Version](https://img.shields.io/badge/version-0.1.3-orange)

---

## 🎬 Demo 演示影片

[![Open Claude Code WPF Demo](https://img.youtube.com/vi/Wp2fpYDPPt0/maxresdefault.jpg)](https://www.youtube.com/watch?v=Wp2fpYDPPt0)

> 點擊上方縮圖或 [前往 YouTube 觀看](https://www.youtube.com/watch?v=Wp2fpYDPPt0)

---

### 🤖 AI 自動填寫表單示範

[![AI 填寫請假單 Demo](https://img.youtube.com/vi/hnivybYTK_0/maxresdefault.jpg)](https://www.youtube.com/watch?v=hnivybYTK_0)

> 示範 AI Agent 自動操作瀏覽器完成請假申請流程。[前往 YouTube 觀看](https://www.youtube.com/watch?v=hnivybYTK_0)

**使用的 Prompt：**
```
幫我開一張請假單,2026/04/11 08:00 ~ 2026/04/12 09:00 ,
請假事由為去銀行辦事情,使用事假,
簽核系統的位置在https://eggeggss.github.io/fake-bpm-for-test/main.html#
登入系統的帳號是rogerroan,密碼是xxxx,不需要跟我確認
```


## 功能特色

- **多供應商支援** — Anthropic Claude、OpenAI GPT、Google Gemini、Ollama（本地）、Azure OpenAI（多節點）
- **串流輸出** — 即時逐 token 顯示回覆
- **延伸思考** — 可折疊的思考過程面板（支援 Claude / o1 / o3 系列）
- **工具呼叫（Function Calling）** — 生成過程中即時顯示工具調用
- **對話歷史** — 本地持久化儲存，側邊欄可搜尋
- **主題切換** — 亮色、暗色、Cloude Code（橘色強調深色主題）
- **Markdown 渲染** — 含語法高亮的程式碼區塊
- **自訂字體** — 字型與大小同時套用至對話區與輸入框
- **模型能力標示** — **工**（工具呼叫）、**視**（視覺）、**T**（延伸思考）

---

## 系統需求

| 項目 | 版本 |
|---|---|
| Windows | 10 / 11 |
| .NET Framework | 4.7.2 或以上 |
| Visual Studio | 2019 / 2022（用於編譯） |

---

## 快速開始

### 1. Clone 專案

```bash
git clone https://github.com/eggeggss/open-claude-code-wpf.git
cd open-claude-code-wpf
```

### 2. 還原 NuGet 套件

在 Visual Studio 中開啟 `OpenClaudeCodeWPF.sln`，然後：

```
工具 → NuGet 套件管理員 → 還原套件
```

或使用 CLI：

```bash
nuget restore OpenClaudeCodeWPF.sln
```

### 3. 設定 API Key

複製範本並填入你的金鑰：

```bash
cp OpenClaudeCodeWPF/App.config.example OpenClaudeCodeWPF/App.config
```

編輯 `App.config`：

```xml
<!-- Anthropic Claude -->
<add key="Anthropic.ApiKey" value="sk-ant-..." />

<!-- OpenAI -->
<add key="OpenAI.ApiKey" value="sk-..." />

<!-- Google Gemini -->
<add key="Gemini.ApiKey" value="AIza..." />

<!-- Ollama（本地，無需 API key） -->
<add key="Ollama.BaseUrl" value="http://localhost:11434" />

<!-- Azure OpenAI（多節點請見下方說明） -->
<add key="AzureOpenAI.ApiKey" value="..." />
<add key="AzureOpenAI.Endpoint" value="https://xxx.openai.azure.com" />
<add key="AzureOpenAI.DeploymentName" value="gpt-4o" />
```

> **提示：** 也可以在程式執行中透過 **設定 → 供應商 API** 輸入金鑰，儲存後立即生效，不需重啟。

### 4. 編譯與執行

在 Visual Studio 中按 **F5**，或：

```bash
msbuild OpenClaudeCodeWPF.sln /p:Configuration=Release
```

執行檔路徑：`OpenClaudeCodeWPF\bin\Release\OpenClaudeCodeWPF.exe`

---

## 📦 打包成 MSI 安裝檔（WiX）

> 若只想在本機執行，可跳過此節。

### 先決條件

1. 安裝 **WiX Toolset v3.11+**：[https://wixtoolset.org/releases/](https://wixtoolset.org/releases/)
2. （選用）在 Visual Studio → Extensions → Manage Extensions 安裝 **WiX Toolset Visual Studio Extension**，可獲得語法提示

### 建置 MSI

**方法 A：Visual Studio**

1. 先對 `OpenClaudeCodeWPF` 主專案 → 右鍵 → **Build**（Release / Any CPU）
2. 再對 `ClaudeCodeWPF.Installer` → 右鍵 → **Build**（Release / x86）
3. 輸出：`ClaudeCodeWPF.Installer\bin\Release\OpenClaudeCodeWPF-Setup.msi`

**方法 B：命令列**

```bat
rem 先建置主專案
msbuild ClaudeCodeWPF\ClaudeCodeWPF.csproj /p:Configuration=Release /p:Platform="Any CPU"

rem 再建置安裝專案
msbuild ClaudeCodeWPF.Installer\ClaudeCodeWPF.Installer.wixproj /p:Configuration=Release /p:Platform=x86
```

### 升版注意事項

1. 更新 `ClaudeCodeWPF/Properties/AssemblyInfo.cs` 的 `AssemblyVersion`
2. 更新 `ClaudeCodeWPF.Installer/Product.wxs` 中的 `<Product Version="X.Y.Z.0" ...>`
3. **不要** 修改 `UpgradeCode` GUID，這是辨識同一產品所有版本的唯一識別碼
4. 重新建置 MSI，舊版本會自動被移除後安裝新版

---

## 設定說明

點擊工具列的 **設定** 按鈕，設定分為四個分頁：

| 分頁 | 內容 |
|---|---|
| 🔑 供應商 API | 各供應商的 API Key、Base URL、Ollama 模型清單、Azure 多節點 |
| ⚙️ 模型參數 | Temperature、Max Tokens、串流開關、語言 |
| 🎨 介面 | 主題切換、字型、字型大小 |
| 📝 系統提示 | 自訂 System Prompt |

設定自動儲存至 `%APPDATA%\OpenClaudeCodeWPF\usersettings.json`。

---

## Azure OpenAI 多節點

在 **設定 → 供應商 API → Azure OpenAI**，每行代表一個部署節點：

```
名稱|Endpoint URL|API金鑰|部署名稱|API版本
East US|https://myhub-eastus.openai.azure.com|sk-xxx|gpt-4o|2024-02-01
Japan East|https://myhub-japan.openai.azure.com|sk-yyy|gpt-4o-mini|2024-02-01
```

儲存後，每個節點會顯示為模型下拉選單中的一個選項。

---

## Ollama（本地模型）

1. 啟動 Ollama：`ollama serve`
2. 下載模型：`ollama pull llama3`
3. 在設定中，將 **Ollama Base URL** 設為 `http://localhost:11434`
4. **自訂模型清單** 留空則自動偵測，或每行填入一個模型 ID：
   ```
   llama3
   mistral
   codellama
   ```

---

## 模型能力標示

| 標示 | 顏色 | 說明 |
|---|---|---|
| **工** | 橘色 | 支援工具呼叫（Function Calling） |
| **視** | 藍色 | 支援視覺 / 多模態輸入 |
| **T** | 深綠色 | 支援延伸思考（Extended Thinking） |

滑鼠移到模型下拉旁的 `工 視 T ?` 圖示可快速查看說明。

---

## 系統架構（MVVM）

本專案採用 **MVVM（Model-View-ViewModel）** 架構，將 UI 邏輯與商業邏輯完全分離。

![MVVM 架構圖](doc/mvvm-architecture.png)

| 層級 | 說明 |
|---|---|
| **Views** | XAML 視圖，僅負責畫面呈現；透過 DataBinding 與 ViewModel 溝通 |
| **ViewModels** | 繼承 `ViewModelBase`（`INotifyPropertyChanged`），持有 UI 狀態與 `ICommand`；不直接操作 View |
| **Services** | 無狀態商業邏輯（API 呼叫、設定讀寫、主題管理等），由 ViewModel 呼叫 |

### ViewModel 對應表

| ViewModel | 對應 View | 主要職責 |
|---|---|---|
| `MainWindowViewModel` | `MainWindow.xaml` | 狀態列、供應商 / 模型選擇、串流開關、Context 指示器 |
| `ChatViewModel` | `ChatPanel.xaml` | 輸入文字、傳送 / 取消命令、Slash 提示顯示 |
| `SettingsViewModel` | `SettingsPanel.xaml` | 所有設定項目（API Key、字型、主題、Temperature）的讀取與儲存 |
| `HistoryViewModel` | `HistoryPanel.xaml` | 對話歷史過濾搜尋、選取後通知 MainWindow 切換 Session |

---

## Agent 推理模式（ReAct）

本專案的 Agent 核心遵循 **ReAct（Reasoning + Acting）** 模式，讓 AI 在每輪回覆中交替進行「推理」與「行動」，直到任務完成。

![ReAct Agent Loop 流程圖](doc/react-flow.png)

### 運作流程

```
用戶輸入
  └─ [Reason] LLM 生成回應（含 tool_calls）
       └─ [Act]   ToolOrchestrator 執行工具，取得 Observation
            └─ [Reason] LLM 看見結果，再次推理
                 └─ [Act]   執行下一批工具
                      └─ ...（最多 20 輪）
                           └─ LLM 不再呼叫工具 → 輸出最終回覆
```

### 兩套 ReAct 實作

| 實作 | 適用情境 | 迭代上限 | 狀態 |
|---|---|---|---|
| **ChatService 主迴圈**（API Function Calling） | Claude / GPT / Gemini 等原生支援工具呼叫的模型 | **20 輪** | ✅ 主流程啟用 |
| **ReActEngine**（純文字格式） | 不支援 Function Calling 的模型（舊版 Ollama） | **15 輪** | ⚠️ 已實作，尚未接入主流程 |

ReActEngine 使用標準文字格式與 LLM 交互：

```
Thought:       [模型的推理過程]
Action:        [工具名稱]
Action Input:  {"param": "value"}
Observation:   [工具執行結果]
... 反覆直到 ...
Final Answer:  [最終回覆]
```

### 子代理（Sub-agent）遞迴

`AgentTool` 讓主 Agent 可以呼叫子代理來完成聚焦型子任務：

```
主 Agent（最多 20 輪）
  └─ 呼叫 AgentTool
       └─ 建立新 ChatService → RunSingleAsync
            └─ 子 Agent（最多 20 輪，同樣擁有所有工具）
                 └─ 可再呼叫 AgentTool → 孫 Agent
                      └─ 理論上無深度上限
```

> **注意**：目前子代理遞迴深度無硬性限制，實際上 LLM 很少自發超過 2～3 層。未來可在 `AgentTool` 加入 `depth` 參數防止無限遞迴。

---

## 專案結構

```
OpenClaudeCodeWPF/
├── Models/              # 資料模型（ChatMessage、ModelInfo、StreamEvent …）
├── Services/
│   ├── Providers/       # 各供應商實作（每個供應商一個檔案）
│   ├── ConfigService.cs # 集中設定管理（App.config + 執行時期覆蓋）
│   ├── UserSettingsService.cs  # 設定持久化至 JSON
│   ├── ThemeService.cs  # 主題管理
│   ├── ChatService.cs   # 供應商呼叫與串流協調
│   └── …
├── ViewModels/          # MVVM ViewModel 層
│   ├── ViewModelBase.cs          # INotifyPropertyChanged 基底類別
│   ├── RelayCommand.cs           # ICommand 實作
│   ├── MainWindowViewModel.cs    # 主視窗狀態
│   ├── ChatViewModel.cs          # 對話輸入狀態與命令
│   ├── SettingsViewModel.cs      # 設定面板狀態
│   └── HistoryViewModel.cs       # 歷史面板狀態
├── Views/
│   ├── ChatPanel.xaml   # 主對話區
│   ├── SettingsPanel.xaml
│   ├── HistoryPanel.xaml
│   └── …
├── App.xaml             # 全域資源與樣式
├── MainWindow.xaml      # 主視窗：工具列 + 供應商 / 模型選擇器
└── App.config           # 預設設定（金鑰填在此或在設定 UI 輸入）
```

---

## 📋 對話日誌 (Log)

每次執行時，程式會自動產生每日日誌：

- **Debug 版本**：exe 所在目錄的 `logs\` 子資料夾
- **Release（安裝版）**：`%AppData%\OpenClaudeCodeWPF\logs\`

```
%AppData%\OpenClaudeCodeWPF\logs\
  2026-04-07.jsonl
  2026-04-08.jsonl
  ...
```

格式為 **JSON Lines**（每行一筆獨立 JSON），方便用任何文字編輯器或工具解析。

### 記錄內容

| type | 說明 |
|------|------|
| `user` | 使用者輸入的訊息 |
| `assistant` | AI 回應內容 |
| `tool_start` | 工具被呼叫（含 input 參數） |
| `tool_done` | 工具執行成功（含結果與耗時 ms） |
| `tool_error` | 工具執行失敗（含錯誤訊息與耗時 ms） |
| `error` | 系統錯誤 |

### 範例

```jsonl
{"time":"2026-04-07T09:10:00.123","type":"user","session":"abc","content":"幫我分析這個檔案"}
{"time":"2026-04-07T09:10:01.456","type":"tool_start","session":"abc","tool":"read_file","id":"tc_001","input":{"path":"C:/foo.txt"}}
{"time":"2026-04-07T09:10:01.890","type":"tool_done","session":"abc","tool":"read_file","id":"tc_001","result":"...","ms":434}
{"time":"2026-04-07T09:10:02.100","type":"assistant","session":"abc","content":"分析結果..."}
```

> 💡 UI 標題列的「📋 日誌」按鈕可直接開啟今日的 log 檔。
> 單一工具結果超過 2000 字元時會自動截斷，避免 log 過大。

---

## 隱私與安全

- **`App.config`** 已加入 `.gitignore`，不會被 commit。請複製 `App.config.example` 為 `App.config` 並在本地填入金鑰。
- 也可在執行時於 **設定 → 供應商 API** 輸入金鑰，儲存至 `%APPDATA%\OpenClaudeCodeWPF\usersettings.json`（同樣 gitignored）。
- 對話歷史僅儲存在本地 `%APPDATA%\OpenClaudeCodeWPF\`。
- 不收集任何遙測資料。

---

## 授權

MIT


---

## Changelog

### v0.1.3
- 新增 OpenRouter 供應商，支援 348 個模型（含 GPT / Claude / Gemini / Llama 等）
- 新增 AI 自動填寫表單 Demo 影片（瀏覽器 Agent 操作請假系統）
- 更新中英文 README

### v0.1.2
- 修正正式版（安裝到 Program Files）啟動時「拒絕存取 logs 路徑」錯誤
- Release 版日誌改存至 `%AppData%\OpenClaudeCodeWPF\logs\`，避免 UAC 寫入限制

### v0.1.1
- 瀏覽器自動化改為 Chrome DevTools Protocol（CDP），不再依賴 Playwright，無需安裝 node.exe
- 新增 `browser_connect` / `browser_fill` / `browser_select` 工具
- `browser_connect` 新增 `wait_ready` 參數，啟動 Edge 後自動等待 CDP 就緒（最多 20 秒）
- Agent 工具迭代上限從 20 提高至 50
- 修正 Agent 中途停止（IsFinalTurn）問題
- 新增 JSON Lines 對話日誌（exe 同目錄 `logs/YYYY-MM-DD.jsonl`）

### v0.1.0
- 初始版本
