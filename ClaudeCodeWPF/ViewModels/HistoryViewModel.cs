using System;
using System.Collections.ObjectModel;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;

namespace OpenClaudeCodeWPF.ViewModels
{
    public class HistoryViewModel : ViewModelBase
    {
        private ConversationManager _manager;

        // ── Displayed sessions (all or filtered) ──────────────────────────
        private ObservableCollection<ConversationSession> _displayedSessions
            = new ObservableCollection<ConversationSession>();

        public ObservableCollection<ConversationSession> DisplayedSessions
        {
            get => _displayedSessions;
            private set => Set(ref _displayedSessions, value);
        }

        // ── Search text ───────────────────────────────────────────────────
        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                Set(ref _searchText, value);
                ApplyFilter(value);
            }
        }

        // ── Selected session ──────────────────────────────────────────────
        private ConversationSession _selectedSession;
        public ConversationSession SelectedSession
        {
            get => _selectedSession;
            set
            {
                Set(ref _selectedSession, value);
                if (value != null)
                    SessionSelected?.Invoke(value);
            }
        }

        /// <summary>Fired when a session row is selected.</summary>
        public event Action<ConversationSession> SessionSelected;

        // ── Public API (called by HistoryPanel code-behind) ───────────────
        public void Initialize(ConversationManager manager)
        {
            _manager = manager;

            // Live update: re-apply filter whenever the session list changes
            manager.Sessions.CollectionChanged += (s, e) => ApplyFilter(SearchText);

            ApplyFilter(null);
        }

        public void Refresh() => ApplyFilter(SearchText);

        // ── Filtering ─────────────────────────────────────────────────────
        private void ApplyFilter(string query)
        {
            if (_manager == null) return;

            if (string.IsNullOrWhiteSpace(query))
            {
                // Point directly at the live ObservableCollection — changes auto-propagate
                DisplayedSessions = _manager.Sessions;
            }
            else
            {
                var lower = query.ToLower();
                var filtered = new ObservableCollection<ConversationSession>();
                foreach (var s in _manager.Sessions)
                {
                    bool titleMatch   = s.Title?.ToLower().Contains(lower) ?? false;
                    bool contentMatch = s.Messages.Count > 0 &&
                        (s.Messages[s.Messages.Count - 1].Content?.ToLower().Contains(lower) ?? false);
                    if (titleMatch || contentMatch)
                        filtered.Add(s);
                }
                DisplayedSessions = filtered;
            }
        }
    }
}
