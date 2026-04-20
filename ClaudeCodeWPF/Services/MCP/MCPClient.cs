using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClaudeCodeWPF.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.MCP
{
    /// <summary>MCP 客戶端 — 支援 stdio 與 HTTP (Streamable HTTP) 傳輸</summary>
    public class MCPClient : IDisposable
    {
        private Process _process;
        private StreamWriter _stdin;
        private StreamReader _stdout;
        private HttpClient _httpClient;
        private string _mcpSessionId; // Streamable HTTP MCP session ID
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

            if (Config.Type == McpServerType.Http || Config.Type == McpServerType.Sse)
            {
                await ConnectHttpAsync(cancellationToken);
            }
            else
            {
                await ConnectStdioAsync(cancellationToken);
            }
        }

        private async Task ConnectStdioAsync(CancellationToken cancellationToken)
        {
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

        private async Task ConnectHttpAsync(CancellationToken cancellationToken)
        {
            // Create HttpClient with SSL validation callback to accept self-signed certs
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
            
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(60);

            // Initialize — server may return Mcp-Session-Id in response headers
            var initResult = await SendRequestAsync("initialize", new JObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JObject { ["tools"] = new JObject() },
                ["clientInfo"] = new JObject { ["name"] = "ClaudeCodeWPF", ["version"] = "1.0" }
            }, cancellationToken);

            if (!initResult.IsSuccess)
                throw new Exception($"MCP init failed: {initResult.Error?.Message}");

            // Send initialized notification (Streamable HTTP MCP protocol)
            await SendHttpNotificationAsync("notifications/initialized", null, cancellationToken);

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

        /// <summary>Send a JSON-RPC notification over HTTP (no id, no response expected)</summary>
        private async Task SendHttpNotificationAsync(string method, JObject @params, CancellationToken cancellationToken)
        {
            var notification = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method
            };
            if (@params != null) notification["params"] = @params;

            var json = notification.ToString(Formatting.None);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, Config.SseUrl);
            httpRequest.Content = content;
            httpRequest.Headers.Add("Accept", "application/json, text/event-stream");

            if (!string.IsNullOrEmpty(_mcpSessionId))
            {
                httpRequest.Headers.Add("Mcp-Session-Id", _mcpSessionId);
            }

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            // Notifications may return 202 Accepted (no content) — that's fine
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
            if (_httpClient != null)
            {
                return await SendHttpRequestAsync(method, @params, cancellationToken);
            }
            else
            {
                return await SendStdioRequestAsync(method, @params, cancellationToken);
            }
        }

        private async Task<MCPResponse> SendStdioRequestAsync(string method, JObject @params, CancellationToken cancellationToken)
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

        private async Task<MCPResponse> SendHttpRequestAsync(string method, JObject @params, CancellationToken cancellationToken)
        {
            var id = _nextId++;
            var request = new MCPRequest { Method = method, Id = id, Params = @params };
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = Config.SseUrl;
            
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = content;
            httpRequest.Headers.Add("Accept", "application/json, text/event-stream");

            // Attach Mcp-Session-Id if we have one (Streamable HTTP MCP protocol)
            if (!string.IsNullOrEmpty(_mcpSessionId))
            {
                httpRequest.Headers.Add("Mcp-Session-Id", _mcpSessionId);
            }

            // Use ResponseHeadersRead so we can start reading the stream immediately
            // without waiting for the server to close the connection (critical for SSE)
            var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // Capture Mcp-Session-Id from response headers
            IEnumerable<string> sessionValues;
            if (response.Headers.TryGetValues("Mcp-Session-Id", out sessionValues))
            {
                _mcpSessionId = sessionValues.FirstOrDefault() ?? _mcpSessionId;
            }

            response.EnsureSuccessStatusCode();

            // Read response as stream — handles SSE servers that keep connections open
            return await ReadSseResponseAsync(response, cancellationToken);
        }

        /// <summary>Read SSE/JSON response from stream, returning as soon as we get a complete data line</summary>
        private async Task<MCPResponse> ReadSseResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                
                // For SSE (text/event-stream), read line by line
                if (contentType.Contains("event-stream"))
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line == null) break; // Connection closed
                        
                        if (line.StartsWith("data:"))
                        {
                            var jsonData = line.Substring(5).Trim();
                            if (!string.IsNullOrEmpty(jsonData))
                            {
                                return JsonConvert.DeserializeObject<MCPResponse>(jsonData);
                            }
                        }
                        // Skip "event:" lines and empty lines
                    }
                    throw new Exception("SSE stream ended without data");
                }
                else
                {
                    // Plain JSON response — read all at once
                    var responseText = await reader.ReadToEndAsync();
                    return JsonConvert.DeserializeObject<MCPResponse>(responseText);
                }
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
            _mcpSessionId = null;

            if (_httpClient != null)
            {
                try { _httpClient.Dispose(); } catch { }
                _httpClient = null;
            }
            else
            {
                try { _stdin?.Close(); } catch { }
                try { if (_process != null && !_process.HasExited) _process.Kill(); } catch { }
            }
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
