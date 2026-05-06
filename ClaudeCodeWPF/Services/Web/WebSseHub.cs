using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.Web
{
    public class WebSseHub
    {
        private static WebSseHub _instance;
        public static WebSseHub Instance => _instance ?? (_instance = new WebSseHub());

        private readonly object _lock = new object();
        private readonly Dictionary<string, List<SseSubscriber>> _subscribers =
            new Dictionary<string, List<SseSubscriber>>(StringComparer.OrdinalIgnoreCase);

        private WebSseHub()
        {
        }

        public async Task SubscribeAsync(string sessionId, HttpListenerContext context, CancellationToken cancellationToken)
        {
            var response = context.Response;
            response.StatusCode = 200;
            response.ContentType = "text/event-stream; charset=utf-8";
            response.Headers["Cache-Control"] = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";
            response.SendChunked = true;

            var subscriber = new SseSubscriber(sessionId, response);
            AddSubscriber(sessionId, subscriber);

            try
            {
                await subscriber.SendAsync("connected", new JObject
                {
                    ["sessionId"] = sessionId
                });

                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                    await subscriber.SendRawAsync(": heartbeat\n\n");
                }
            }
            catch
            {
                // Client disconnected; cleanup happens below.
            }
            finally
            {
                RemoveSubscriber(sessionId, subscriber);
                subscriber.Close();
            }
        }

        public async Task BroadcastAsync(string sessionId, string eventName, JObject data)
        {
            List<SseSubscriber> snapshot;
            lock (_lock)
            {
                if (!_subscribers.TryGetValue(sessionId, out var list) || list.Count == 0)
                    return;
                snapshot = new List<SseSubscriber>(list);
            }

            var dead = new List<SseSubscriber>();
            foreach (var subscriber in snapshot)
            {
                try
                {
                    await subscriber.SendAsync(eventName, data ?? new JObject());
                }
                catch
                {
                    dead.Add(subscriber);
                }
            }

            if (dead.Count > 0)
            {
                lock (_lock)
                {
                    if (_subscribers.TryGetValue(sessionId, out var list))
                    {
                        foreach (var subscriber in dead)
                            list.Remove(subscriber);
                    }
                }

                foreach (var subscriber in dead)
                    subscriber.Close();
            }
        }

        private void AddSubscriber(string sessionId, SseSubscriber subscriber)
        {
            lock (_lock)
            {
                if (!_subscribers.TryGetValue(sessionId, out var list))
                {
                    list = new List<SseSubscriber>();
                    _subscribers[sessionId] = list;
                }
                list.Add(subscriber);
            }
        }

        private void RemoveSubscriber(string sessionId, SseSubscriber subscriber)
        {
            lock (_lock)
            {
                if (!_subscribers.TryGetValue(sessionId, out var list))
                    return;
                list.Remove(subscriber);
                if (list.Count == 0)
                    _subscribers.Remove(sessionId);
            }
        }

        private class SseSubscriber
        {
            private readonly HttpListenerResponse _response;
            private readonly StreamWriter _writer;
            private readonly object _writeLock = new object();
            private bool _closed;

            public SseSubscriber(string sessionId, HttpListenerResponse response)
            {
                SessionId = sessionId;
                _response = response;
                _writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false));
            }

            public string SessionId { get; private set; }

            public Task SendAsync(string eventName, JObject data)
            {
                var json = JsonConvert.SerializeObject(data ?? new JObject(), Formatting.None);
                return SendRawAsync("event: " + eventName + "\n" + "data: " + json + "\n\n");
            }

            public Task SendRawAsync(string text)
            {
                lock (_writeLock)
                {
                    if (_closed) throw new ObjectDisposedException("SseSubscriber");
                    _writer.Write(text);
                    _writer.Flush();
                    return Task.CompletedTask;
                }
            }

            public void Close()
            {
                lock (_writeLock)
                {
                    if (_closed) return;
                    _closed = true;
                    try { _writer.Dispose(); } catch { }
                    try { _response.Close(); } catch { }
                }
            }
        }
    }
}
