using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services.Skills
{
    /// <summary>
    /// 管理使用者自製技能的生命週期：載入、匯入、刪除、啟用／停用。
    /// 支援三種格式：
    ///   - .json  — 自訂格式（SkillDefinition JSON）
    ///   - .md    — Anthropic SKILL.md 格式（YAML frontmatter + Markdown body）
    ///   - .zip   — 解壓後自動找 SKILL.md
    /// 技能儲存於 %AppData%\OpenClaudeCodeWPF\skills\。
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

        // ── 載入 ─────────────────────────────────────────────────────────────

        public void LoadSkills()
        {
            _skills.Clear();
            try
            {
                Directory.CreateDirectory(SkillsDir);

                // JSON skills
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
                    catch { }
                }

                // Directory skills (SKILL.md inside subdirectories or zip-extracted folders)
                foreach (var dir in Directory.GetDirectories(SkillsDir))
                {
                    var mdPath = FindSkillMd(dir);
                    if (mdPath == null) continue;
                    try
                    {
                        var (skill, _) = ParseSkillMd(mdPath);
                        if (skill != null && skill.IsValid)
                        {
                            skill.FilePath    = mdPath;
                            skill.IsDirectory = true;
                            skill.DirPath     = dir;
                            _skills.Add(skill);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 若啟用中的技能已被刪除，清除它
            if (ActiveSkill != null && !_skills.Exists(s => s.Name == ActiveSkill.Name))
                ActiveSkill = null;

            SkillsChanged?.Invoke();
        }

        // ── 匯入 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 匯入技能。支援 .json / .md / .zip。
        /// 回傳 null 表示成功；非 null 為錯誤訊息。
        /// </summary>
        public string ImportSkill(string sourcePath)
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            switch (ext)
            {
                case ".json": return ImportSkillFromJson(sourcePath);
                case ".md":   return ImportSkillFromMd(sourcePath);
                case ".zip":  return ImportSkillFromZip(sourcePath);
                default:
                    return "不支援的檔案格式。\n請選擇 .json、.md 或 .zip 技能檔案。";
            }
        }

        private string ImportSkillFromJson(string sourcePath)
        {
            try
            {
                var json  = File.ReadAllText(sourcePath);
                var skill = JsonConvert.DeserializeObject<SkillDefinition>(json);

                if (skill == null || !skill.IsValid)
                    return "技能檔案格式無效：缺少 \"name\" 或 \"systemPrompt\" 欄位。\n\n" +
                           "JSON 範例：\n{\n  \"name\": \"技能名稱\",\n  \"systemPrompt\": \"你是...\"\n}";

                Directory.CreateDirectory(SkillsDir);
                var destPath = Path.Combine(SkillsDir, MakeSafeFileName(skill.Name) + ".json");
                File.WriteAllText(destPath, JsonConvert.SerializeObject(skill, Formatting.Indented));

                LoadSkills();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private string ImportSkillFromMd(string sourcePath)
        {
            try
            {
                var (skill, err) = ParseSkillMd(sourcePath);
                if (skill == null) return err;

                Directory.CreateDirectory(SkillsDir);
                var dirName = MakeSafeFileName(skill.Name);
                var destDir = Path.Combine(SkillsDir, dirName);
                Directory.CreateDirectory(destDir);

                File.Copy(sourcePath, Path.Combine(destDir, "SKILL.md"), overwrite: true);

                LoadSkills();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private string ImportSkillFromZip(string zipPath)
        {
            try
            {
                var zipName  = Path.GetFileNameWithoutExtension(zipPath);
                var safeName = MakeSafeFileName(zipName);
                var destDir  = Path.Combine(SkillsDir, safeName);

                if (Directory.Exists(destDir))
                    Directory.Delete(destDir, recursive: true);
                Directory.CreateDirectory(destDir);

                // Extract — use ZipArchive (no need for ZipFile extension methods)
                using (var stream  = File.OpenRead(zipPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue; // directory marker

                        // Normalise path separators and resolve to destDir
                        var relPath  = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        var fullPath = Path.GetFullPath(Path.Combine(destDir, relPath));

                        // Guard against zip-slip
                        if (!fullPath.StartsWith(Path.GetFullPath(destDir) + Path.DirectorySeparatorChar))
                            continue;

                        var entryDir = Path.GetDirectoryName(fullPath);
                        if (!Directory.Exists(entryDir))
                            Directory.CreateDirectory(entryDir);

                        using (var src  = entry.Open())
                        using (var dest = File.Create(fullPath))
                            src.CopyTo(dest);
                    }
                }

                var mdPath = FindSkillMd(destDir);
                if (mdPath == null)
                {
                    Directory.Delete(destDir, recursive: true);
                    return "ZIP 中找不到 SKILL.md 檔案。\n\n請確認 ZIP 包含 SKILL.md 且格式正確。";
                }

                var (skill, parseErr) = ParseSkillMd(mdPath);
                if (skill == null)
                {
                    Directory.Delete(destDir, recursive: true);
                    return parseErr;
                }

                LoadSkills();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ── 刪除 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 刪除技能。回傳 null 表示成功；非 null 為錯誤訊息。
        /// 無論檔案是否成功刪除，都會從記憶體清單中移除並觸發 SkillsChanged。
        /// </summary>
        public string DeleteSkill(SkillDefinition skill)
        {
            string error = null;
            try
            {
                if (skill.IsDirectory)
                {
                    if (!string.IsNullOrEmpty(skill.DirPath) && Directory.Exists(skill.DirPath))
                        Directory.Delete(skill.DirPath, recursive: true);
                }
                else
                {
                    if (!string.IsNullOrEmpty(skill.FilePath) && File.Exists(skill.FilePath))
                        File.Delete(skill.FilePath);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            // 即使刪除失敗，也先清除啟用狀態，再重新掃描磁碟
            if (ActiveSkill?.Name == skill.Name)
                ActiveSkill = null;

            LoadSkills(); // 重新掃描，若刪除失敗技能會再次出現
            return error;
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

        /// <summary>取得目前啟用技能的系統提示詞，無技能時回傳 null</summary>
        public string GetActiveSkillPrompt() => ActiveSkill?.SystemPrompt;

        // ── SKILL.md 解析 ─────────────────────────────────────────────────────

        /// <summary>
        /// 在指定目錄（含子目錄）中尋找 SKILL.md，回傳第一個找到的路徑。
        /// </summary>
        private static string FindSkillMd(string dir)
        {
            // Direct child first
            var direct = Path.Combine(dir, "SKILL.md");
            if (File.Exists(direct)) return direct;

            // One level deeper (e.g. zip extracted as skill-name/SKILL.md)
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var nested = Path.Combine(sub, "SKILL.md");
                if (File.Exists(nested)) return nested;
            }
            return null;
        }

        /// <summary>
        /// 解析 SKILL.md。格式：
        ///   ---
        ///   name: ...
        ///   description: ...
        ///   ---
        ///   # Body used as systemPrompt
        /// 回傳 (SkillDefinition, errorMessage)。
        /// </summary>
        private static (SkillDefinition, string) ParseSkillMd(string mdPath)
        {
            string content;
            try   { content = File.ReadAllText(mdPath, Encoding.UTF8); }
            catch (Exception ex) { return (null, ex.Message); }

            if (!content.TrimStart().StartsWith("---"))
                return (null, "SKILL.md 格式無效：第一行應為 --- (YAML 前置設定開始)。");

            // Find end of frontmatter
            var firstDash = content.IndexOf("---");
            var endDash   = content.IndexOf("\n---", firstDash + 3);
            if (endDash < 0)
                return (null, "SKILL.md 格式無效：找不到 YAML 前置設定的結尾 ---。");

            var frontmatter = content.Substring(firstDash + 3, endDash - firstDash - 3);
            var body        = content.Substring(endDash + 4).TrimStart('\r', '\n');

            string name = null, description = null, icon = null;

            foreach (var rawLine in frontmatter.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("name:"))
                    name = line.Substring(5).Trim().Trim('"', '\'');
                else if (line.StartsWith("description:"))
                    description = line.Substring(12).Trim().Trim('"', '\'');
                else if (line.StartsWith("icon:"))
                    icon = line.Substring(5).Trim().Trim('"', '\'');
            }

            if (string.IsNullOrWhiteSpace(name))
                return (null, "SKILL.md 的 YAML 前置設定中缺少 'name' 欄位。");

            if (string.IsNullOrWhiteSpace(body))
                return (null, "SKILL.md 的 body（技能指令）不能為空。");

            var skill = new SkillDefinition
            {
                Name         = name,
                Description  = description ?? "",
                Icon         = string.IsNullOrEmpty(icon) ? "⚡" : icon,
                SystemPrompt = body,
                Version      = "1.0"
            };

            return (skill, null);
        }

        // ── 輔助 ──────────────────────────────────────────────────────────────

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c.ToString(), "_");
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }
    }
}
