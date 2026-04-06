using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Utils;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>歷史記錄服務 — 持久化對話摘要和元資料</summary>
    public class HistoryService
    {
        private static HistoryService _instance;
        public static HistoryService Instance => _instance ?? (_instance = new HistoryService());

        private readonly string _historyPath;
        private List<HistoryEntry> _entries;

        private HistoryService()
        {
            _historyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeCodeWPF", "history.json");
            Load();
        }

        public List<HistoryEntry> GetAll() => _entries ?? (_entries = new List<HistoryEntry>());

        public void AddEntry(ConversationSession session)
        {
            var entries = GetAll();
            // Remove existing entry for this session
            entries.RemoveAll(e => e.SessionId == session.Id);

            entries.Insert(0, new HistoryEntry
            {
                SessionId = session.Id,
                Title = session.Title,
                Provider = session.Provider,
                Model = session.Model,
                MessageCount = session.Messages.Count,
                LastMessage = GetLastUserMessage(session),
                UpdatedAt = session.UpdatedAt
            });

            // Keep only last 1000 entries
            if (entries.Count > 1000)
                entries.RemoveRange(1000, entries.Count - 1000);

            Save();
        }

        public void RemoveEntry(string sessionId)
        {
            GetAll().RemoveAll(e => e.SessionId == sessionId);
            Save();
        }

        public List<HistoryEntry> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return GetAll();

            var q = query.ToLower();
            return GetAll().FindAll(e =>
                (e.Title?.ToLower().Contains(q) ?? false) ||
                (e.LastMessage?.ToLower().Contains(q) ?? false));
        }

        private string GetLastUserMessage(ConversationSession session)
        {
            for (int i = session.Messages.Count - 1; i >= 0; i--)
            {
                if (session.Messages[i].Role == "user")
                {
                    var content = session.Messages[i].Content ?? "";
                    return content.Length > 100 ? content.Substring(0, 100) + "..." : content;
                }
            }
            return "";
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_historyPath))
                {
                    var json = File.ReadAllText(_historyPath);
                    _entries = JsonConvert.DeserializeObject<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
                }
                else _entries = new List<HistoryEntry>();
            }
            catch { _entries = new List<HistoryEntry>(); }
        }

        private void Save()
        {
            try
            {
                FileSystemHelper.EnsureDirectory(Path.GetDirectoryName(_historyPath));
                File.WriteAllText(_historyPath, JsonConvert.SerializeObject(_entries, Formatting.Indented));
            }
            catch { }
        }
    }

    public class HistoryEntry
    {
        public string SessionId { get; set; }
        public string Title { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public int MessageCount { get; set; }
        public string LastMessage { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
