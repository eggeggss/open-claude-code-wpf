using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Browser
{
    public class CdpTab
    {
        public string Id    { get; set; }
        public string Title { get; set; }
        public string Url   { get; set; }
        public string WebSocketDebuggerUrl { get; set; }
    }

    /// <summary>
    /// Chrome DevTools Protocol 瀏覽器服務。
    /// 使用前：以 --remote-debugging-port=9222 啟動 Chrome/Edge。
    /// 不需要安裝任何 NuGet 套件，純用 .NET 內建 WebSocket。
    /// </summary>
    public class CdpBrowserService : IDisposable
    {
        private static CdpBrowserService _instance;
        public static CdpBrowserService Instance
            => _instance ?? (_instance = new CdpBrowserService());

        private ClientWebSocket _ws;
        private int _msgId;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JObject>> _pending
            = new ConcurrentDictionary<int, TaskCompletionSource<JObject>>();
        private CancellationTokenSource _recvCts;

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public string CurrentUrl   { get; private set; } = "";
        public string CurrentTitle { get; private set; } = "";
        public int    DebugPort    { get; private set; } = 9222;

        private CdpBrowserService() { }

        // ── Tab listing ───────────────────────────────────────────────────────
        /// <param name="maxWaitMs">啟動後最多等待毫秒數（Edge/Chrome 剛啟動時使用）</param>
        public async Task<List<CdpTab>> GetTabsAsync(int port = 9222, int maxWaitMs = 12000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
            int attempt  = 0;
            Exception lastEx = null;
            while (true)
            {
                attempt++;
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) })
                    {
                        var json = await http.GetStringAsync($"http://localhost:{port}/json");
                        var arr  = JArray.Parse(json);
                        var list = new List<CdpTab>();
                        foreach (var t in arr)
                        {
                            if (t["type"]?.ToString() != "page") continue;
                            list.Add(new CdpTab
                            {
                                Id                   = t["id"]?.ToString(),
                                Title                = t["title"]?.ToString(),
                                Url                  = t["url"]?.ToString(),
                                WebSocketDebuggerUrl = t["webSocketDebuggerUrl"]?.ToString()
                            });
                        }
                        return list;
                    }
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    if (DateTime.UtcNow >= deadline)
                        throw new Exception($"無法連接 CDP port {port}（嘗試 {attempt} 次）: {ex.Message}", ex);
                    await Task.Delay(1500);
                }
            }
        }

        // ── Connect ───────────────────────────────────────────────────────────
        /// <param name="wsUrl">指定分頁 WebSocket URL；null = 自動選第一個 page 分頁</param>
        /// <param name="tabUrl">含此字串的分頁優先選取</param>
        public async Task ConnectAsync(string wsUrl = null, int port = 9222, string tabUrl = null)
        {
            await DisconnectAsync();
            DebugPort = port;

            if (wsUrl == null)
            {
                var tabs = await GetTabsAsync(port);
                if (tabs.Count == 0)
                    throw new InvalidOperationException(
                        $"找不到可用的分頁。請先以 --remote-debugging-port={port} 啟動 Chrome/Edge。");

                // Prefer tab whose URL contains tabUrl hint
                CdpTab chosen = null;
                if (!string.IsNullOrEmpty(tabUrl))
                    chosen = tabs.Find(t => t.Url != null && t.Url.Contains(tabUrl));
                wsUrl = (chosen ?? tabs[0]).WebSocketDebuggerUrl;
            }

            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
            _recvCts = new CancellationTokenSource();
            Task.Run(() => ReceiveLoopAsync(_recvCts.Token));

            await SendCommandAsync("Page.enable", null, 5000);
            await UpdatePageInfoAsync();
        }

        public async Task DisconnectAsync()
        {
            _recvCts?.Cancel();
            try
            {
                if (_ws?.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { }
            _ws?.Dispose();
            _ws = null;
            foreach (var kv in _pending) kv.Value.TrySetCanceled();
            _pending.Clear();
        }

        // ── CDP command ───────────────────────────────────────────────────────
        public async Task<JObject> SendCommandAsync(string method, JObject parms = null, int timeoutMs = 20000)
        {
            if (!IsConnected)
                throw new InvalidOperationException("CDP 未連線，請先執行 browser_connect");

            var id  = Interlocked.Increment(ref _msgId);
            var msg = new JObject { ["id"] = id, ["method"] = method };
            if (parms != null) msg["params"] = parms;

            var tcs = new TaskCompletionSource<JObject>();
            _pending[id] = tcs;

            var bytes = Encoding.UTF8.GetBytes(msg.ToString(Formatting.None));
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                cts.Token.Register(() => tcs.TrySetCanceled());
                return await tcs.Task;
            }
        }

        // ── High-level helpers ────────────────────────────────────────────────
        public async Task<string> NavigateAsync(string url)
        {
            await SendCommandAsync("Page.navigate", new JObject { ["url"] = url });
            await Task.Delay(1800); // wait for page load
            await UpdatePageInfoAsync();
            return $"已導航至: {CurrentTitle}\nURL: {CurrentUrl}";
        }

        /// <summary>執行 JS，回傳 returnValue（returnByValue=true）</summary>
        public async Task<JToken> EvaluateAsync(string expression, bool awaitPromise = false, int timeoutMs = 15000)
        {
            var res = await SendCommandAsync("Runtime.evaluate", new JObject
            {
                ["expression"]    = expression,
                ["returnByValue"] = true,
                ["awaitPromise"]  = awaitPromise
            }, timeoutMs);

            // Check for JS exception
            var exc = res["result"]?["exceptionDetails"];
            if (exc != null)
                throw new Exception("JS 錯誤: " + (exc["exception"]?["description"] ?? exc["text"]));

            return res["result"]?["result"]?["value"];
        }

        public async Task UpdatePageInfoAsync()
        {
            try
            {
                CurrentTitle = (await EvaluateAsync("document.title"))?.ToString()          ?? "";
                CurrentUrl   = (await EvaluateAsync("window.location.href"))?.ToString()    ?? "";
            }
            catch { }
        }

        public async Task<string> GetPageTextAsync()
            => (await EvaluateAsync("document.body ? document.body.innerText : ''"))?.ToString() ?? "";

        public async Task<string> GetSimplifiedDomAsync()
        {
            const string js = @"
(function(){
    function walk(el, depth){
        if(depth>6) return '';
        var tag = el.tagName ? el.tagName.toLowerCase() : '';
        var skip = ['script','style','svg','head','noscript','meta','link'];
        if(skip.indexOf(tag)>=0) return '';
        var id = el.id ? '#'+el.id : '';
        var cls = el.className && typeof el.className==='string' ? '.'+el.className.split(' ').slice(0,2).join('.') : '';
        var txt = '';
        var child = '';
        for(var i=0;i<el.childNodes.length;i++){
            var n=el.childNodes[i];
            if(n.nodeType===3){var t=n.textContent.trim(); if(t) txt+=' '+t.substring(0,60);}
            else if(n.nodeType===1) child+=walk(n,depth+1);
        }
        var line = tag+id+cls;
        if(el.value!==undefined && el.value!=='') line+=' val='+String(el.value).substring(0,40);
        if(el.placeholder) line+=' ph='+el.placeholder.substring(0,40);
        if(el.href) line+=' href='+el.href.substring(0,60);
        if(txt.trim()) line+=txt.substring(0,80);
        return '<'+line+'>'+child+'</'+tag+'>\n';
    }
    return walk(document.body,0).substring(0,8000);
})()";
            return (await EvaluateAsync(js))?.ToString() ?? "";
        }

        public async Task<string> ClickAsync(string selector)
        {
            var js = $@"
(function(){{
    var el=document.querySelector({JsonConvert.SerializeObject(selector)});
    if(!el) return 'NOT_FOUND';
    el.scrollIntoView({{block:'center'}});
    el.focus();
    el.click();
    return 'OK:'+el.tagName+'|'+(el.textContent||'').trim().substring(0,50);
}})()";
            var r = (await EvaluateAsync(js))?.ToString() ?? "";
            if (r.StartsWith("NOT_FOUND")) throw new Exception($"找不到元素: {selector}");
            return r;
        }

        public async Task<string> FillAsync(string selector, string value)
        {
            var js = $@"
(function(){{
    var el=document.querySelector({JsonConvert.SerializeObject(selector)});
    if(!el) return 'NOT_FOUND';
    el.focus();
    var proto = el.tagName==='TEXTAREA'
        ? window.HTMLTextAreaElement.prototype
        : window.HTMLInputElement.prototype;
    var setter = Object.getOwnPropertyDescriptor(proto,'value');
    if(setter && setter.set) setter.set.call(el,{JsonConvert.SerializeObject(value)});
    else el.value = {JsonConvert.SerializeObject(value)};
    el.dispatchEvent(new Event('input',{{bubbles:true}}));
    el.dispatchEvent(new Event('change',{{bubbles:true}}));
    return 'OK';
}})()";
            var r = (await EvaluateAsync(js))?.ToString() ?? "";
            if (r == "NOT_FOUND") throw new Exception($"找不到元素: {selector}");
            return $"已填入 '{value}' → {selector}";
        }

        public async Task<string> SelectAsync(string selector, string value)
        {
            var js = $@"
(function(){{
    var el=document.querySelector({JsonConvert.SerializeObject(selector)});
    if(!el) return 'NOT_FOUND';
    // Try matching by value, then by text
    var found=false;
    for(var i=0;i<el.options.length;i++){{
        if(el.options[i].value==={JsonConvert.SerializeObject(value)} ||
           el.options[i].text==={JsonConvert.SerializeObject(value)}){{
            el.selectedIndex=i; found=true; break;
        }}
    }}
    if(!found) return 'OPTION_NOT_FOUND:'+el.options.length+' options';
    el.dispatchEvent(new Event('change',{{bubbles:true}}));
    return 'OK:'+el.options[el.selectedIndex].text;
}})()";
            var r = (await EvaluateAsync(js))?.ToString() ?? "";
            if (r == "NOT_FOUND") throw new Exception($"找不到元素: {selector}");
            if (r.StartsWith("OPTION_NOT_FOUND")) throw new Exception($"選項 '{value}' 不存在，{r}");
            return $"已選取 '{r.Substring(3)}' → {selector}";
        }

        public async Task<string> FindElementsAsync(string selector)
        {
            var js = $@"
(function(){{
    var els=document.querySelectorAll({JsonConvert.SerializeObject(selector)});
    if(els.length===0) return '找不到任何元素';
    var lines=[];
    Array.from(els).slice(0,30).forEach(function(el,i){{
        var info='['+i+'] <'+el.tagName.toLowerCase()+'>';
        if(el.id) info+=' id='+el.id;
        if(el.name) info+=' name='+el.name;
        if(el.type) info+=' type='+el.type;
        if(el.value!==undefined&&el.value!=='') info+=' value='+String(el.value).substring(0,40);
        if(el.placeholder) info+=' placeholder='+el.placeholder.substring(0,40);
        var txt=(el.textContent||'').trim().substring(0,60);
        if(txt) info+=' text='+txt;
        info+=' visible='+(!!el.offsetParent);
        lines.push(info);
    }});
    return '找到 '+els.length+' 個元素:\n'+lines.join('\n');
}})()";
            return (await EvaluateAsync(js))?.ToString() ?? "";
        }

        public async Task<string> ScrollAsync(int x, int y)
        {
            await EvaluateAsync($"window.scrollBy({x},{y})");
            return $"已滾動 x={x} y={y}";
        }

        public async Task<string> TypeTextAsync(string text)
        {
            // Dispatch keypress events on focused element
            foreach (var ch in text)
            {
                var code = ((int)ch).ToString();
                await SendCommandAsync("Input.dispatchKeyEvent", new JObject
                {
                    ["type"] = "keyDown",
                    ["text"] = ch.ToString()
                });
                await SendCommandAsync("Input.dispatchKeyEvent", new JObject
                {
                    ["type"] = "char",
                    ["text"] = ch.ToString()
                });
                await SendCommandAsync("Input.dispatchKeyEvent", new JObject
                {
                    ["type"] = "keyUp",
                    ["text"] = ch.ToString()
                });
            }
            return $"已輸入: {text}";
        }

        public async Task<string> PressKeyAsync(string key)
        {
            // key = "Enter", "Tab", "Escape", etc.
            await SendCommandAsync("Input.dispatchKeyEvent", new JObject
            {
                ["type"] = "keyDown",
                ["key"]  = key,
                ["code"] = "Key" + key
            });
            await SendCommandAsync("Input.dispatchKeyEvent", new JObject
            {
                ["type"] = "keyUp",
                ["key"]  = key,
                ["code"] = "Key" + key
            });
            return $"已按鍵: {key}";
        }

        public async Task<string> CaptureScreenshotAsync(string savePath = null)
        {
            var res  = await SendCommandAsync("Page.captureScreenshot", new JObject { ["format"] = "png" }, 30000);
            var data = res["result"]?["data"]?.ToString();
            if (data == null) throw new Exception("截圖失敗");
            var bytes = Convert.FromBase64String(data);
            if (savePath == null)
                savePath = Path.Combine(Path.GetTempPath(), $"cdp_screenshot_{DateTime.Now:yyyyMMddHHmmss}.png");
            File.WriteAllBytes(savePath, bytes);
            return savePath;
        }

        // ── Receive loop ──────────────────────────────────────────────────────
        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buf = new byte[4096];
            try
            {
                while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
                {
                    using (var ms = new MemoryStream())
                    {
                        WebSocketReceiveResult r;
                        do
                        {
                            r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                            if (r.MessageType == WebSocketMessageType.Close) return;
                            ms.Write(buf, 0, r.Count);
                        } while (!r.EndOfMessage);

                        try
                        {
                            var obj = JObject.Parse(Encoding.UTF8.GetString(ms.ToArray()));
                            var id  = obj["id"]?.Value<int>();
                            if (id.HasValue && _pending.TryRemove(id.Value, out var tcs))
                                tcs.TrySetResult(obj);
                            // events (method field present) are ignored for now
                        }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        public void Dispose() => DisconnectAsync().GetAwaiter().GetResult();
    }
}
