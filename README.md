# Open Claude Code WPF

> 基於 WPF（.NET Framework 4.7.2）開發的 Windows 桌面 AI 對話用戶端，整合多家 AI 供應商於單一介面。

📖 [English Version](README.en.md)

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET%20Framework-4.7.2-purple)
![Language](https://img.shields.io/badge/language-C%23%207.3-brightgreen)

---

## 主畫面

![Open Claude Code WPF 主畫面](doc/entry.png)

---

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
git clone <repo-url>
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

## 隱私與安全

- **`App.config`** 已加入 `.gitignore`，不會被 commit。請複製 `App.config.example` 為 `App.config` 並在本地填入金鑰。
- 也可在執行時於 **設定 → 供應商 API** 輸入金鑰，儲存至 `%APPDATA%\OpenClaudeCodeWPF\usersettings.json`（同樣 gitignored）。
- 對話歷史僅儲存在本地 `%APPDATA%\OpenClaudeCodeWPF\`。
- 不收集任何遙測資料。

---

## 授權

MIT

