using System;
using System.Windows.Controls;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;
using OpenClaudeCodeWPF.ViewModels;

namespace OpenClaudeCodeWPF.Views
{
    public partial class HistoryPanel : UserControl
    {
        private readonly HistoryViewModel _vm;

        public event Action<ConversationSession> OnSessionSelected;

        public HistoryPanel()
        {
            InitializeComponent();
            _vm = new HistoryViewModel();
            DataContext = _vm;
            _vm.SessionSelected += s => OnSessionSelected?.Invoke(s);
        }

        public void Initialize(ConversationManager manager) => _vm.Initialize(manager);

        public void Refresh() => _vm.Refresh();
    }
}

