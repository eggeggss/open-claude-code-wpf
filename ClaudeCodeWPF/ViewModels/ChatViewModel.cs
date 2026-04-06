using System;

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
        public bool CanSend => !string.IsNullOrWhiteSpace(InputText) && !IsSending;
        public bool IsSlashHintVisible => InputText?.StartsWith("/") ?? false;

        // ── Commands ──────────────────────────────────────────────────────
        public RelayCommand SendCommand { get; }
        public RelayCommand CancelCommand { get; }

        // ── Events (consumed by ChatPanel code-behind) ────────────────────
        /// <summary>Fired with the trimmed text when user sends a normal message.</summary>
        public event Action<string> SendRequested;

        /// <summary>Fired with the slash command string (e.g. "/clear").</summary>
        public event Action<string> SlashCommandRequested;

        /// <summary>Fired when user requests cancellation.</summary>
        public event Action CancelRequested;

        // ── Constructor ───────────────────────────────────────────────────
        public ChatViewModel()
        {
            SendCommand   = new RelayCommand(ExecuteSend,   () => CanSend);
            CancelCommand = new RelayCommand(ExecuteCancel, () => IsSending);
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private void ExecuteSend()
        {
            var text = InputText?.Trim();
            if (string.IsNullOrEmpty(text)) return;

            InputText = "";

            if (text.StartsWith("/"))
            {
                SlashCommandRequested?.Invoke(text);
                return;
            }

            IsSending = true;
            SendRequested?.Invoke(text);
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
