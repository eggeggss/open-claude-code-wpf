using System;
using System.Windows;
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
        public event Action OnToggleRequested;

        public HistoryPanel()
        {
            InitializeComponent();
            _vm = new HistoryViewModel();
            DataContext = _vm;
            _vm.SessionSelected += s => OnSessionSelected?.Invoke(s);
        }

        public void Initialize(ConversationManager manager) => _vm.Initialize(manager);

        public void Refresh() => _vm.Refresh();

        /// <summary>Update collapse button icon to reflect current sidebar state.</summary>
        public void SetCollapsed(bool collapsed)
        {
            CollapseBtn.Content  = collapsed ? "▶" : "◀";
            CollapseBtn.ToolTip  = collapsed ? "展開側欄" : "摺疊側欄";
        }

        private void CollapseBtn_Click(object sender, RoutedEventArgs e)
        {
            OnToggleRequested?.Invoke();
        }
    }
}

