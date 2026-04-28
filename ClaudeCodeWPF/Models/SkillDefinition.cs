using Newtonsoft.Json;

namespace OpenClaudeCodeWPF.Models
{
    /// <summary>
    /// 使用者自製技能的定義（儲存為 .json 檔案）。
    /// 技能啟用後，其 SystemPrompt 會附加到基礎系統提示詞，
    /// 讓 AI 具備特定的行為模式或專業知識。
    /// </summary>
    public class SkillDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>單一 Emoji 圖示，顯示在技能卡片上</summary>
        [JsonProperty("icon")]
        public string Icon { get; set; } = "⚡";

        /// <summary>啟用此技能時附加到系統提示詞的內容</summary>
        [JsonProperty("systemPrompt")]
        public string SystemPrompt { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("author")]
        public string Author { get; set; }

        /// <summary>執行期：技能檔案的磁碟路徑（不序列化）</summary>
        [JsonIgnore]
        public string FilePath { get; set; }

        /// <summary>執行期：若技能來自目錄（SKILL.md），為 true</summary>
        [JsonIgnore]
        public bool IsDirectory { get; set; }

        /// <summary>執行期：目錄型技能的資料夾路徑（用於刪除整個目錄）</summary>
        [JsonIgnore]
        public string DirPath { get; set; }

        [JsonIgnore]
        public bool IsValid =>
            !string.IsNullOrWhiteSpace(Name) &&
            !string.IsNullOrWhiteSpace(SystemPrompt);
    }
}
