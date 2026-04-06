using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.MCP
{
    /// <summary>MCP 客戶端 — 通過 stdio 與 MCP 伺服器通訊</summary>
    public class MCPClient : IDisposable
    {
        private Process _process;
        private StreamWriter _stdin;
        private StreamReader _stdout;
        private int _nextId = 1;
        private readonly Dictionary<int, TaskCompletionSource<MCPResponse>> _pending = new Dictionary<int, TaskCompletionSource<MCPResponse>>();
        private Task _readLoop;
        private CancellationTokenSource _cts;
        private bool _disposed;

        public MCPServerConfig Config { get; }
        public bool IsConnected { get; private set; }
        public MCPServerInfo ServerInfo { get; private set; }

        public MCPClient(MCPServerConfig config)
        {
            Config = config;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            _cts = new CancellationTokenSource();

            var psi = new ProcessStartInfo
            {
                FileName = Config.Command,
                Arguments = string.Join(" ", Config.Args),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            foreach (var env in Config.Env)
                psi.EnvironmentVariables[env.Key] = env.Value;

            _process = new Process { StartInfo = psi };
            _process.Start();

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            // Start background read loop
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));

            // Initialize
            var initResult = await SendRequestAsync("initialize", new JObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JObject { ["tools"] = new JObject() },
                ["clientInfo"] = new JObject { ["name"] = "ClaudeCodeWPF", ["version"] = "1.0" }
            }, cancellationToken);

            if (!initResult.IsSuccess)
                throw new Exception($"MCP init failed: {initResult.Error?.Message}");

            // Send initialized notification
            await SendNotificationAsync("notifications/initialized");

            // Fetch tools
            var toolsResult = await SendRequestAsync("tools/list", null, cancellationToken);
            var tools = toolsResult.Result?["tools"]?.ToObject<List<MCPTool>>() ?? new List<MCPTool>();

            ServerInfo = new MCPServerInfo
            {
                Name = Config.Name,
                Tools = tools
            };

            IsConnected = true;
        }

        public async Task<JObject> CallToolAsync(string toolName, JObject arguments, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await SendRequestAsync("tools/call", new JObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments ?? new JObject()
            }, cancellationToken);

            if (!result.IsSuccess)
                throw new Exception($"Tool call failed: {result.Error?.Message}");

            return result.Result;
        }

        public async Task<string> ReadResourceAsync(string uri, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = await SendRequestAsync("resources/read", new JObject { ["uri"] = uri }, cancellationToken);
            if (!result.IsSuccess)
                throw new Exception($"Resource read failed: {result.Error?.Message}");

            var contents = result.Result?["contents"] as JArray;
            if (contents == null || contents.Count == 0) return "";

            return contents[0]["text"]?.ToString() ?? "";
        }

        private async Task<MCPResponse> SendRequestAsync(string method, JObject @params, CancellationToken cancellationToken)
        {
            var id = _nextId++;
            var tcs = new TaskCompletionSource<MCPResponse>();

            lock (_pending)
                _pending[id] = tcs;

            var request = new MCPRequest { Method = method, Id = id, Params = @params };
            var json = JsonConvert.SerializeObject(request);

            await _stdin.WriteLineAsync(json);
            await _stdin.FlushAsync();

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }

        private async Task SendNotificationAsync(string method, JObject @params = null)
        {
            var notification = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method
            };
            if (@params != null) notification["params"] = @params;

            await _stdin.WriteLineAsync(notification.ToString(Formatting.None));
            await _stdin.FlushAsync();
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !_process.HasExited)
            {
                try
                {
                    var line = await _stdout.ReadLineAsync();
                    if (line == null) break;

                    var response = JsonConvert.DeserializeObject<MCPResponse>(line);
                    if (response?.Id != null)
                    {
                        var id = Convert.ToInt32(response.Id);
                        TaskCompletionSource<MCPResponse> tcs;
                        lock (_pending)
                        {
                            if (_pending.TryGetValue(id, out tcs))
                                _pending.Remove(id);
                            else tcs = null;
                        }
                        tcs?.TrySetResult(response);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* ignore parse errors */ }
            }
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            IsConnected = false;

            try { _stdin?.Close(); } catch { }
            try { if (!_process.HasExited) _process.Kill(); } catch { }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _process?.Dispose();
                _cts?.Dispose();
                _disposed = true;
            }
        }
    }
}
