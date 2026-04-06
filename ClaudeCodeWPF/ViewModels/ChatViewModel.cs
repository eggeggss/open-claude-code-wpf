using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        // ── Input text ────────────────────────────────────────────────────
        private string _inputText = "";
        public string InputText
        {
            get => _inputText;
            set
            {
                Set(ref _inputText, value);
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(IsSlashHintVisible));
            }
        }

        // ── Attached files ────────────────────────────────────────────────
        public ObservableCollection<AttachedFileInfo> AttachedFiles { get; }
            = new ObservableCollection<AttachedFileInfo>();

        public bool HasAttachedFiles => AttachedFiles.Count > 0;

        // ── Sending state ─────────────────────────────────────────────────
        private bool _isSending;
        public bool IsSending
        {
            get => _isSending;
            set
            {
                Set(ref _isSending, value);
                OnPropertyChanged(nameof(CanSend));
            }
        }

        // ── Computed ──────────────────────────────────────────────────────
        public bool CanSend => (!string.IsNullOrWhiteSpace(InputText) || HasAttachedFiles) && !IsSending;
        public bool IsSlashHintVisible => InputText?.StartsWith("/") ?? false;

        // ── Commands ──────────────────────────────────────────────────────
        public RelayCommand SendCommand { get; }
        public RelayCommand CancelCommand { get; }

        // ── Events (consumed by ChatPanel code-behind) ────────────────────
        /// <summary>Fired with trimmed text + attached files when user sends a message.</summary>
        public event Action<string, IReadOnlyList<AttachedFileInfo>> SendRequested;

        /// <summary>Fired with the slash command string (e.g. "/clear").</summary>
        public event Action<string> SlashCommandRequested;

        /// <summary>Fired when user requests cancellation.</summary>
        public event Action CancelRequested;

        // ── Constructor ───────────────────────────────────────────────────
        public ChatViewModel()
        {
            SendCommand   = new RelayCommand(ExecuteSend,   () => CanSend);
            CancelCommand = new RelayCommand(ExecuteCancel, () => IsSending);

            AttachedFiles.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(HasAttachedFiles));
                OnPropertyChanged(nameof(CanSend));
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────
        public void AddFiles(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                // Avoid duplicates
                foreach (var existing in AttachedFiles)
                    if (existing.FilePath == path) goto next;

                AttachedFiles.Add(new AttachedFileInfo
                {
                    FilePath = path,
                    FileSize = new FileInfo(path).Length
                });
                next:;
            }
        }

        public void RemoveFile(AttachedFileInfo file)
        {
            AttachedFiles.Remove(file);
        }

        private void ExecuteSend()
        {
            var text = InputText?.Trim() ?? "";

            if (string.IsNullOrEmpty(text) && AttachedFiles.Count == 0) return;

            InputText = "";

            if (text.StartsWith("/"))
            {
                SlashCommandRequested?.Invoke(text);
                return;
            }

            var files = new List<AttachedFileInfo>(AttachedFiles);
            AttachedFiles.Clear();

            IsSending = true;
            SendRequested?.Invoke(text, files);
        }

        private void ExecuteCancel()
        {
            CancelRequested?.Invoke();
        }

        /// <summary>
        /// Called by ChatPanel when the operation completes (or errors),
        /// re-enables send and clears the sending flag.
        /// </summary>
        public void NotifySendComplete()
        {
            IsSending = false;
        }
    }
}

