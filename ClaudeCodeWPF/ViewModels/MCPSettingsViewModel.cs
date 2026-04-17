using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using OpenClaudeCodeWPF.Services;
using OpenClaudeCodeWPF.Services.MCP;

namespace OpenClaudeCodeWPF.ViewModels
{
    public class MCPSettingsViewModel : ViewModelBase
    {
        private MCPServerItemViewModel _selectedServer;
        private MCPServerItemViewModel _editingServer;
        private bool _isAddingNew;

        public ObservableCollection<MCPServerItemViewModel> Servers { get; }
            = new ObservableCollection<MCPServerItemViewModel>();

        public MCPServerItemViewModel SelectedServer
        {
            get => _selectedServer;
            set
            {
                if (Set(ref _selectedServer, value))
                {
                    CancelEdit(); // discard unsaved changes when switching
                    if (value != null)
                        EditingServer = CloneForEdit(value);
                    else
                        EditingServer = null;

                    OnPropertyChanged(nameof(HasSelection));
                }
            }
        }

        /// <summary>Working copy shown in the right-side form</summary>
        public MCPServerItemViewModel EditingServer
        {
            get => _editingServer;
            set => Set(ref _editingServer, value);
        }

        public bool HasSelection => SelectedServer != null;
        public bool IsAddingNew
        {
            get => _isAddingNew;
            set { Set(ref _isAddingNew, value); OnPropertyChanged(nameof(FormTitle)); }
        }

        public string FormTitle => IsAddingNew ? "新增 MCP 伺服器" : "編輯 MCP 伺服器";

        // ─── Commands ────────────────────────────────────────────────
        public ICommand AddNewCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ToggleEnabledCommand { get; }

        public MCPSettingsViewModel()
        {
            AddNewCommand = new RelayCommand(OnAddNew);
            SaveCommand = new RelayCommand(OnSave, () => EditingServer != null);
            DeleteCommand = new RelayCommand(OnDelete, () => SelectedServer != null);
            ConnectCommand = new RelayCommand(OnConnect, () => SelectedServer != null && !SelectedServer.IsConnected);
            DisconnectCommand = new RelayCommand(OnDisconnect, () => SelectedServer != null && SelectedServer.IsConnected);
            CancelCommand = new RelayCommand(OnCancel);
            ToggleEnabledCommand = new RelayCommand<MCPServerItemViewModel>(OnToggleEnabled);

            Load();
        }

        // ─── Load ────────────────────────────────────────────────────
        public void Load()
        {
            Servers.Clear();
            foreach (var cfg in MCPConfigService.Instance.LoadAll())
            {
                var vm = MCPServerItemViewModel.FromConfig(cfg);
                // Reflect live connection status
                var client = MCPConnectionManager.Instance.GetClient(cfg.Name);
                if (client != null && client.IsConnected)
                    vm.Status = McpServerStatus.Connected;
                Servers.Add(vm);
            }
        }

        // ─── Add / Save / Delete ─────────────────────────────────────
        private void OnAddNew()
        {
            IsAddingNew = true;
            SelectedServer = null;
            EditingServer = new MCPServerItemViewModel { IsEditing = true };
        }

        private void OnSave()
        {
            if (EditingServer == null) return;

            if (string.IsNullOrWhiteSpace(EditingServer.Name))
            {
                MessageBox.Show("Server Name 不能為空", "驗證錯誤", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cfg = EditingServer.ToConfig();

            if (IsAddingNew)
            {
                // Check duplicate name
                if (Servers.Any(s => s.Name == cfg.Name))
                {
                    MessageBox.Show($"已存在名稱為「{cfg.Name}」的伺服器", "重複名稱", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                MCPConfigService.Instance.Add(cfg);
                var newVm = MCPServerItemViewModel.FromConfig(cfg);
                Servers.Add(newVm);
                SelectedServer = newVm;
                IsAddingNew = false;
            }
            else
            {
                MCPConfigService.Instance.Update(cfg);
                // Refresh selected server from edited copy
                var idx = Servers.IndexOf(SelectedServer);
                if (idx >= 0)
                {
                    var refreshed = MCPServerItemViewModel.FromConfig(cfg);
                    refreshed.Status = SelectedServer.Status;
                    Servers[idx] = refreshed;
                    SelectedServer = Servers[idx];
                }
            }
        }

        private void OnDelete()
        {
            if (SelectedServer == null) return;

            var r = MessageBox.Show($"確定刪除「{SelectedServer.Name}」？", "確認刪除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            if (SelectedServer.IsConnected)
                MCPConnectionManager.Instance.DisconnectAsync(SelectedServer.Name);

            MCPConfigService.Instance.Delete(SelectedServer.Name);
            Servers.Remove(SelectedServer);
            SelectedServer = Servers.FirstOrDefault();
        }

        // ─── Connect / Disconnect ────────────────────────────────────
        private async void OnConnect()
        {
            if (SelectedServer == null) return;
            var vm = SelectedServer;
            vm.Status = McpServerStatus.Connecting;

            try
            {
                var cfg = MCPConfigService.Instance.LoadAll().FirstOrDefault(s => s.Name == vm.Name)
                    ?? vm.ToConfig();
                await MCPConnectionManager.Instance.ConnectAsync(cfg, CancellationToken.None);
                vm.Status = McpServerStatus.Connected;
            }
            catch (Exception ex)
            {
                vm.Status = McpServerStatus.Error;
                MessageBox.Show($"連接失敗：{ex.Message}", "MCP 連接錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnDisconnect()
        {
            if (SelectedServer == null) return;
            var vm = SelectedServer;
            await MCPConnectionManager.Instance.DisconnectAsync(vm.Name);
            vm.Status = McpServerStatus.Disconnected;
        }

        // ─── Toggle Enabled ──────────────────────────────────────────
        private void OnToggleEnabled(MCPServerItemViewModel vm)
        {
            if (vm == null) return;
            vm.Enabled = !vm.Enabled;
            MCPConfigService.Instance.SetEnabled(vm.Name, vm.Enabled);
        }

        // ─── Cancel / helpers ────────────────────────────────────────
        private void OnCancel()
        {
            CancelEdit();
            IsAddingNew = false;
            if (SelectedServer != null)
                EditingServer = CloneForEdit(SelectedServer);
        }

        private void CancelEdit()
        {
            if (EditingServer != null)
                EditingServer.IsEditing = false;
        }

        private static MCPServerItemViewModel CloneForEdit(MCPServerItemViewModel src)
        {
            var clone = MCPServerItemViewModel.FromConfig(src.ToConfig());
            clone.Status = src.Status;
            clone.IsEditing = true;
            return clone;
        }

        // ─── Called from outside (auto-connect on startup) ───────────
        public async void AutoConnectEnabled()
        {
            foreach (var cfg in MCPConfigService.Instance.LoadAll())
            {
                if (!cfg.Enabled) continue;
                var vm = Servers.FirstOrDefault(s => s.Name == cfg.Name);
                if (vm != null) vm.Status = McpServerStatus.Connecting;

                try
                {
                    await MCPConnectionManager.Instance.ConnectAsync(cfg, CancellationToken.None);
                    if (vm != null) vm.Status = McpServerStatus.Connected;
                }
                catch
                {
                    if (vm != null) vm.Status = McpServerStatus.Error;
                }
            }
        }
    }
}
