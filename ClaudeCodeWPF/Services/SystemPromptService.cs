using System;
using System.Collections.Generic;
using System.IO;
using OpenClaudeCodeWPF.Utils;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>系統提示詞服務</summary>
    public class SystemPromptService
    {
        private static SystemPromptService _instance;
        public static SystemPromptService Instance => _instance ?? (_instance = new SystemPromptService());

        private static readonly string DEFAULT_SYSTEM_PROMPT_ZH = @"你是 Claude，一個由 Anthropic 訓練的 AI 助手。你能幫助用戶完成各種程式設計任務，包括：
- 撰寫、閱讀和修改程式碼
- 執行 bash 命令
- 搜尋和分析檔案
- 瀏覽網頁
- 管理和組織程式碼庫

你的工作風格：
- 先思考後行動
- 盡可能使用工具來完成任務
- 提供清晰、準確的程式碼
- 主動發現並修復問題
- 詢問澄清問題，而不是做出假設

格式規範（非常重要）：
- 所有程式碼片段必須使用 markdown 程式碼圍欄格式，並標註語言，例如：
  ```csharp
  // 程式碼
  ```
- Shell 指令也要使用圍欄：```bash ... ```
- 不可以裸輸出程式碼，必須用 ``` 包裹

請用繁體中文與用戶溝通，除非用戶用英文提問。";

        private static readonly string DEFAULT_SYSTEM_PROMPT_EN = @"You are Claude, an AI assistant trained by Anthropic. You help users with software engineering tasks including:
- Writing, reading, and modifying code
- Executing bash commands
- Searching and analyzing files
- Browsing the web
- Managing and organizing codebases

Your working style:
- Think before acting
- Use tools whenever possible
- Provide clear, accurate code
- Proactively identify and fix issues
- Ask clarifying questions rather than making assumptions

Formatting rules (important):
- Always wrap ALL code snippets in markdown fenced code blocks with the language tag, e.g.:
  ```csharp
  // code here
  ```
- Shell commands must also be fenced: ```bash ... ```
- Never output bare unwrapped code — always use ``` fences.";

        private string _customPrompt;

        public string GetSystemPrompt(string language = "zh-TW")
        {
            if (!string.IsNullOrEmpty(_customPrompt))
                return _customPrompt;

            return language.StartsWith("zh") ? DEFAULT_SYSTEM_PROMPT_ZH : DEFAULT_SYSTEM_PROMPT_EN;
        }

        public void SetCustomPrompt(string prompt)
        {
            _customPrompt = prompt;
        }

        public void ClearCustomPrompt()
        {
            _customPrompt = null;
        }

        public string LoadFromFile(string path)
        {
            var content = FileSystemHelper.SafeReadAllText(path);
            if (!string.IsNullOrEmpty(content))
            {
                _customPrompt = content;
                return content;
            }
            return null;
        }
    }
}
