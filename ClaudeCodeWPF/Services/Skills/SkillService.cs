using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services.Skills
{
    /// <summary>
    /// 管理使用者自製技能（SkillDefinition）的生命週期：
    /// 載入、匯入、刪除、啟用／停用。
    /// 技能檔案儲存於 %AppData%\OpenClaudeCodeWPF\skills\*.json。
    /// </summary>
    public class SkillService
    {
        private static SkillService _instance;
        public static SkillService Instance => _instance ?? (_instance = new SkillService());

        public static readonly string SkillsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "OpenClaudeCodeWPF", "skills");

        private readonly List<SkillDefinition> _skills = new List<SkillDefinition>();

        public IReadOnlyList<SkillDefinition> AllSkills => _skills.AsReadOnly();

        public SkillDefinition ActiveSkill { get; private set; }

        /// <summary>技能清單或啟用狀態變更時觸發</summary>
        public event Action SkillsChanged;

        private SkillService()
        {
            LoadSkills();
        }

        // ── 載入 ────────────────────────────────────────────────────────────

        public void LoadSkills()
        {
            _skills.Clear();
            try
            {
                Directory.CreateDirectory(SkillsDir);
                foreach (var file in Directory.GetFiles(SkillsDir, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var skill = JsonConvert.DeserializeObject<SkillDefinition>(json);
                        if (skill != null && skill.IsValid)
                        {
                            skill.FilePath = file;
                            _skills.Add(skill);
                        }
                    }
                    catch { /* 略過格式錯誤的檔案 */ }
                }
            }
            catch { }

            // 若啟用中的技能已被刪除，清除它
            if (ActiveSkill != null && !_skills.Exists(s => s.Name == ActiveSkill.Name))
                ActiveSkill = null;

            SkillsChanged?.Invoke();
        }

        // ── 匯入 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 從磁碟路徑匯入技能檔案，複製到 SkillsDir 並重新載入清單。
        /// 回傳 null 表示成功；非 null 為錯誤訊息。
        /// </summary>
        public string ImportSkill(string sourcePath)
        {
            try
            {
                var json = File.ReadAllText(sourcePath);
                var skill = JsonConvert.DeserializeObject<SkillDefinition>(json);

                if (skill == null || !skill.IsValid)
                    return "技能檔案格式無效：缺少 \"name\" 或 \"systemPrompt\" 欄位。\n\n" +
                           "請確認 JSON 格式正確，例如：\n" +
                           "{\n  \"name\": \"技能名稱\",\n  \"systemPrompt\": \"你是...\"\n}";

                Directory.CreateDirectory(SkillsDir);

                // 用技能名稱作為檔名（轉換非法字元）
                var safeName = MakeSafeFileName(skill.Name);
                var destPath = Path.Combine(SkillsDir, safeName + ".json");

                // 寫入整潔的 JSON（重新序列化，移除執行期欄位）
                File.WriteAllText(destPath,
                    JsonConvert.SerializeObject(skill, Formatting.Indented));

                LoadSkills();
                return null; // success
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ── 刪除 ────────────────────────────────────────────────────────────

        public void DeleteSkill(SkillDefinition skill)
        {
            try
            {
                if (!string.IsNullOrEmpty(skill.FilePath) && File.Exists(skill.FilePath))
                    File.Delete(skill.FilePath);
            }
            catch { }

            if (ActiveSkill?.Name == skill.Name)
                ActiveSkill = null;

            LoadSkills();
        }

        // ── 啟用 / 停用 ──────────────────────────────────────────────────────

        public void ActivateSkill(SkillDefinition skill)
        {
            ActiveSkill = skill;
            SkillsChanged?.Invoke();
        }

        public void DeactivateSkill()
        {
            ActiveSkill = null;
            SkillsChanged?.Invoke();
        }

        /// <summary>取得目前啟用技能的系統提示詞片段，無技能時回傳 null</summary>
        public string GetActiveSkillPrompt() => ActiveSkill?.SystemPrompt;

        // ── 輔助 ────────────────────────────────────────────────────────────

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "_");
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }
    }
}
