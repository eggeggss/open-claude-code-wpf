using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.MCP
{
    /// <summary>管理多個 MCP 伺服器連接</summary>
    public class MCPConnectionManager : IDisposable
    {
        private static MCPConnectionManager _instance;
        public static MCPConnectionManager Instance => _instance ?? (_instance = new MCPConnectionManager());

        private readonly Dictionary<string, MCPClient> _clients = new Dictionary<string, MCPClient>();
        private bool _disposed;

        public event Action<string> OnServerConnected;
        public event Action<string> OnServerDisconnected;
        public event Action<string, string> OnServerError;

        public async Task ConnectAsync(MCPServerConfig config, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_clients.ContainsKey(config.Name))
                await DisconnectAsync(config.Name);

            var client = new MCPClient(config);
            try
            {
                await client.ConnectAsync(cancellationToken);
                _clients[config.Name] = client;
                RegisterMCPTools(client);
                OnServerConnected?.Invoke(config.Name);
            }
            catch (Exception ex)
            {
                client.Dispose();
                OnServerError?.Invoke(config.Name, ex.Message);
                throw;
            }
        }

        public Task DisconnectAsync(string serverName)
        {
            if (_clients.TryGetValue(serverName, out var client))
            {
                client.Disconnect();
                client.Dispose();
                _clients.Remove(serverName);
                UnregisterMCPTools(serverName);
                OnServerDisconnected?.Invoke(serverName);
            }
            return Task.CompletedTask;
        }

        public List<MCPClient> GetConnectedClients()
        {
            return _clients.Values.Where(c => c.IsConnected).ToList();
        }

        public MCPClient GetClient(string serverName)
        {
            _clients.TryGetValue(serverName, out var client);
            return client;
        }

        private void RegisterMCPTools(MCPClient client)
        {
            if (client.ServerInfo == null) return;

            foreach (var mcpTool in client.ServerInfo.Tools)
            {
                var bridge = new MCPToolBridge(mcpTool, client);
                ToolRegistry.Instance.Register(bridge);
            }
        }

        private void UnregisterMCPTools(string serverName)
        {
            // Tools will still be in registry but client won't respond
            // A proper implementation would remove them
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var client in _clients.Values)
                    client.Dispose();
                _clients.Clear();
                _disposed = true;
            }
        }
    }
}
