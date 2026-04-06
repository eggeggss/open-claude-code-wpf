using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Utils;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>對話管理器 — 管理多個對話會話的生命週期</summary>
    public class ConversationManager
    {
        private static ConversationManager _instance;
        public static ConversationManager Instance => _instance ?? (_instance = new ConversationManager());

        private readonly string _storagePath;
        private ConversationSession _activeSession;

        public ObservableCollection<ConversationSession> Sessions { get; } = new ObservableCollection<ConversationSession>();
        public ConversationSession ActiveSession => _activeSession;

        public event Action<ConversationSession> OnSessionChanged;
        public event Action<ConversationSession> OnSessionCreated;
        public event Action<string> OnSessionDeleted;

        private ConversationManager()
        {
            _storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeCodeWPF", "conversations");
            FileSystemHelper.EnsureDirectory(_storagePath);
            LoadSessions();
        }

        public ConversationSession CreateSession(string title = null)
        {
            var session = new ConversationSession
            {
                Id = Guid.NewGuid().ToString(),
                Title = title ?? "新對話",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Provider = ConfigService.Instance.CurrentProvider,
                Model = ConfigService.Instance.CurrentModel
            };

            Sessions.Insert(0, session);
            SetActiveSession(session);
            OnSessionCreated?.Invoke(session);
            return session;
        }

        public void SetActiveSession(ConversationSession session)
        {
            _activeSession = session;
            OnSessionChanged?.Invoke(session);
        }

        public void DeleteSession(string sessionId)
        {
            var session = FindSession(sessionId);
            if (session != null)
            {
                Sessions.Remove(session);
                var filePath = GetSessionFilePath(sessionId);
                if (File.Exists(filePath)) File.Delete(filePath);

                if (_activeSession?.Id == sessionId)
                {
                    _activeSession = Sessions.Count > 0 ? Sessions[0] : null;
                    OnSessionChanged?.Invoke(_activeSession);
                }

                OnSessionDeleted?.Invoke(sessionId);
            }
        }

        public void SaveSession(ConversationSession session)
        {
            if (session == null) return;
            session.UpdatedAt = DateTime.Now;

            var json = JsonConvert.SerializeObject(session, Formatting.Indented);
            File.WriteAllText(GetSessionFilePath(session.Id), json);
        }

        public void SaveActiveSession()
        {
            if (_activeSession != null) SaveSession(_activeSession);
        }

        public ConversationSession GetOrCreateActiveSession()
        {
            if (_activeSession == null)
                CreateSession();
            return _activeSession;
        }

        private ConversationSession FindSession(string id)
        {
            foreach (var s in Sessions)
                if (s.Id == id) return s;
            return null;
        }

        private void LoadSessions()
        {
            if (!Directory.Exists(_storagePath)) return;

            var files = Directory.GetFiles(_storagePath, "*.json");
            var loaded = new List<ConversationSession>();

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var session = JsonConvert.DeserializeObject<ConversationSession>(json);
                    if (session != null) loaded.Add(session);
                }
                catch { /* skip corrupted files */ }
            }

            // Sort by UpdatedAt descending
            loaded.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
            foreach (var s in loaded)
                Sessions.Add(s);

            if (Sessions.Count > 0)
                _activeSession = Sessions[0];
        }

        private string GetSessionFilePath(string id)
        {
            return Path.Combine(_storagePath, $"{id}.json");
        }
    }
}
