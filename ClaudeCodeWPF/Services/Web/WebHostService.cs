using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OpenClaudeCodeWPF.Services.Web
{
    public class WebHostService
    {
        private static WebHostService _instance;
        public static WebHostService Instance => _instance ?? (_instance = new WebHostService());

        private readonly object _lock = new object();
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _listenTask;
        private string _prefix;

        // Login cookie tokens: cookie value → expiry (session lifetime in memory)
        private readonly HashSet<string> _loginTokens = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _loginLock = new object();

        private WebHostService()
        {
        }

        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    return _listener != null && _listener.IsListening;
                }
            }
        }

        public void EnsureStarted()
        {
            if (!ConfigService.Instance.WebHostEnabled)
                throw new InvalidOperationException("Web Session 功能已在 App.config 停用。");

            lock (_lock)
            {
                var prefix = BuildPrefix();
                if (_listener != null && _listener.IsListening && string.Equals(_prefix, prefix, StringComparison.OrdinalIgnoreCase))
                    return;

                StopLocked();

                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add(prefix);

                try
                {
                    _listener.Start();
                    _prefix = prefix;
                }
                catch (HttpListenerException ex)
                {
                    StopLocked();
                    throw new InvalidOperationException(BuildStartErrorMessage(prefix, ex), ex);
                }

                _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                StopLocked();
            }
            // Clear login tokens so a new server start forces re-login
            lock (_loginLock)
            {
                _loginTokens.Clear();
            }
        }

        private void StopLocked()
        {
            if (_listener == null)
                return;

            try { _cts?.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
            _listener = null;
            _cts = null;
            _listenTask = null;
            _prefix = null;
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context = null;
                try
                {
                    context = await _listener.GetContextAsync();
                    Task.Run(() => HandleContextAsync(context, cancellationToken));
                }
                catch
                {
                    if (!cancellationToken.IsCancellationRequested && context != null)
                        SafeClose(context.Response);
                }
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var responseClosedByHandler = false;
            try
            {
                var request = context.Request;
                var route = ParseRoute(request.Url.AbsolutePath);
                if (route == null)
                {
                    WriteText(context.Response, 404, "Not found");
                    return;
                }

                // ── Login / Logout routes (no auth required) ──────────────────
                if (route.Kind == WebRouteKind.Login)
                {
                    if (request.HttpMethod == "POST")
                        await HandleLoginPostAsync(context, request);
                    else
                        HandleLoginGet(context, request.Url.Query);
                    return;
                }

                if (route.Kind == WebRouteKind.Logout)
                {
                    HandleLogout(context);
                    return;
                }

                // ── Login protection middleware ────────────────────────────────
                if (ConfigService.Instance.WebLoginEnabled && !IsLoginCookieValid(request))
                {
                    var next = Uri.EscapeDataString(request.Url.PathAndQuery);
                    Redirect(context.Response, "/login?next=" + next);
                    return;
                }

                // ── Per-session token validation ───────────────────────────────
                if (!ValidateRequestToken(route.SessionId, request))
                {
                    WriteText(context.Response, 403, "Invalid or missing session token");
                    return;
                }

                if (route.Kind == WebRouteKind.Page && request.HttpMethod == "GET")
                {
                    WriteHtml(context.Response, BuildHtml());
                    return;
                }

                if (route.Kind == WebRouteKind.State && request.HttpMethod == "GET")
                {
                    WriteJson(context.Response, WebChatBridge.Instance.GetState(route.SessionId));
                    return;
                }

                if (route.Kind == WebRouteKind.Send && request.HttpMethod == "POST")
                {
                    var body = await ReadBodyAsync(request);
                    var obj = string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
                    var message = obj["message"]?.ToString();
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        WriteText(context.Response, 400, "message is required");
                        return;
                    }

                    await WebChatBridge.Instance.StartSendAsync(route.SessionId, message);
                    WriteJson(context.Response, new JObject { ["ok"] = true });
                    return;
                }

                if (route.Kind == WebRouteKind.Cancel && request.HttpMethod == "POST")
                {
                    var cancelled = WebChatBridge.Instance.Cancel(route.SessionId);
                    WriteJson(context.Response, new JObject { ["ok"] = true, ["cancelled"] = cancelled });
                    return;
                }

                if (route.Kind == WebRouteKind.Events && request.HttpMethod == "GET")
                {
                    responseClosedByHandler = true;
                    await WebSseHub.Instance.SubscribeAsync(route.SessionId, context, cancellationToken);
                    return;
                }

                WriteText(context.Response, 405, "Method not allowed");
            }
            catch (Exception ex)
            {
                if (!responseClosedByHandler)
                    WriteJson(context.Response, new JObject { ["ok"] = false, ["error"] = ex.Message }, 500);
            }
            finally
            {
                if (!responseClosedByHandler)
                    SafeClose(context.Response);
            }
        }

        // ── Login handlers ────────────────────────────────────────────────────

        private void HandleLoginGet(HttpListenerContext context, string queryString)
        {
            var form = ParseFormString(queryString?.TrimStart('?') ?? "");
            var next = form.ContainsKey("next") ? form["next"] : "";
            WriteHtml(context.Response, BuildLoginHtml(next, null));
        }

        private async Task HandleLoginPostAsync(HttpListenerContext context, HttpListenerRequest request)
        {
            var body = await ReadBodyAsync(request);
            var form = ParseFormString(body ?? "");
            var username = form.ContainsKey("username") ? form["username"] : "";
            var password = form.ContainsKey("password") ? form["password"] : "";
            var next     = form.ContainsKey("next")     ? form["next"]     : "/";

            var cfg = ConfigService.Instance;
            bool ok = string.Equals(username, cfg.WebLoginUsername, StringComparison.Ordinal)
                   && string.Equals(password, cfg.WebLoginPassword, StringComparison.Ordinal);

            if (ok)
            {
                var token = GenerateLoginToken();
                lock (_loginLock) { _loginTokens.Add(token); }
                context.Response.Headers["Set-Cookie"] =
                    "wauth=" + token + "; HttpOnly; Path=/; SameSite=Strict";
                Redirect(context.Response, string.IsNullOrWhiteSpace(next) ? "/" : next);
            }
            else
            {
                WriteHtml(context.Response, BuildLoginHtml(next, "帳號或密碼錯誤，請重試。"));
            }
        }

        private void HandleLogout(HttpListenerContext context)
        {
            // Expire the cookie
            var cookieVal = GetCookieValue(context.Request, "wauth");
            if (cookieVal != null)
                lock (_loginLock) { _loginTokens.Remove(cookieVal); }

            context.Response.Headers["Set-Cookie"] =
                "wauth=; HttpOnly; Path=/; Max-Age=0; SameSite=Strict";
            Redirect(context.Response, "/login");
        }

        private bool IsLoginCookieValid(HttpListenerRequest request)
        {
            var val = GetCookieValue(request, "wauth");
            if (string.IsNullOrEmpty(val)) return false;
            lock (_loginLock) { return _loginTokens.Contains(val); }
        }

        private static string GetCookieValue(HttpListenerRequest request, string name)
        {
            var header = request.Headers["Cookie"];
            if (string.IsNullOrEmpty(header)) return null;
            foreach (var part in header.Split(';'))
            {
                var kv = part.Trim().Split(new[] { '=' }, 2);
                if (kv.Length == 2 && string.Equals(kv[0].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return kv[1].Trim();
            }
            return null;
        }

        private static string GenerateLoginToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static void Redirect(HttpListenerResponse response, string location)
        {
            response.StatusCode = 302;
            response.Headers["Location"] = location;
        }

        /// <summary>Parses application/x-www-form-urlencoded or query strings.</summary>
        private static Dictionary<string, string> ParseFormString(string s)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(s)) return dict;
            foreach (var pair in s.Split('&'))
            {
                var idx = pair.IndexOf('=');
                if (idx < 0) continue;
                var key = Uri.UnescapeDataString(pair.Substring(0, idx).Replace('+', ' '));
                var val = Uri.UnescapeDataString(pair.Substring(idx + 1).Replace('+', ' '));
                dict[key] = val;
            }
            return dict;
        }

        private static string HtmlEncode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&#39;");
        }

        private bool ValidateRequestToken(string sessionId, HttpListenerRequest request)
        {
            return WebSessionManager.Instance.ValidateToken(sessionId, request.QueryString["token"]);
        }

        private static WebRoute ParseRoute(string absolutePath)
        {
            var path = (absolutePath ?? "").Trim('/');
            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            // /login
            if (segments.Length == 1 && string.Equals(segments[0], "login", StringComparison.OrdinalIgnoreCase))
                return new WebRoute(null, WebRouteKind.Login);

            // /logout
            if (segments.Length == 1 && string.Equals(segments[0], "logout", StringComparison.OrdinalIgnoreCase))
                return new WebRoute(null, WebRouteKind.Logout);

            if (segments.Length < 2 || !string.Equals(segments[0], "sessions", StringComparison.OrdinalIgnoreCase))
                return null;

            var sessionId = Uri.UnescapeDataString(segments[1]);
            if (string.IsNullOrWhiteSpace(sessionId))
                return null;

            if (segments.Length == 2)
                return new WebRoute(sessionId, WebRouteKind.Page);

            if (segments.Length == 3 && string.Equals(segments[2], "events", StringComparison.OrdinalIgnoreCase))
                return new WebRoute(sessionId, WebRouteKind.Events);

            if (segments.Length == 4 && string.Equals(segments[2], "api", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(segments[3], "state", StringComparison.OrdinalIgnoreCase))
                    return new WebRoute(sessionId, WebRouteKind.State);
                if (string.Equals(segments[3], "send", StringComparison.OrdinalIgnoreCase))
                    return new WebRoute(sessionId, WebRouteKind.Send);
                if (string.Equals(segments[3], "cancel", StringComparison.OrdinalIgnoreCase))
                    return new WebRoute(sessionId, WebRouteKind.Cancel);
            }

            return null;
        }

        private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8, true))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private static void WriteJson(HttpListenerResponse response, JObject data, int statusCode = 200)
        {
            var json = JsonConvert.SerializeObject(data ?? new JObject(), Formatting.None);
            WriteBytes(response, statusCode, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
        }

        private static void WriteHtml(HttpListenerResponse response, string html)
        {
            WriteBytes(response, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
        }

        private static void WriteText(HttpListenerResponse response, int statusCode, string text)
        {
            WriteBytes(response, statusCode, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text ?? ""));
        }

        private static void WriteBytes(HttpListenerResponse response, int statusCode, string contentType, byte[] bytes)
        {
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static void SafeClose(HttpListenerResponse response)
        {
            try { response.Close(); } catch { }
        }

        private static string BuildPrefix()
        {
            var host = ConfigService.Instance.WebHostHost;
            if (string.IsNullOrWhiteSpace(host) ||
                host == "0.0.0.0" ||
                host == "*" ||
                host == "+")
            {
                host = "+";
            }

            return "http://" + host + ":" + ConfigService.Instance.WebHostPort + "/";
        }

        private static string BuildStartErrorMessage(string prefix, HttpListenerException ex)
        {
            return "無法啟動 Web Session server (" + prefix + ")。\n\n"
                + "常見原因：port 80 被占用、沒有系統管理員權限，或尚未設定 URL ACL。\n"
                + "Windows 可用系統管理員 PowerShell 執行：\n"
                + "netsh http add urlacl url=" + prefix + " user=Everyone\n\n"
                + "原始錯誤：" + ex.Message;
        }

        private static string BuildHtml()
        {
            return @"<!doctype html>
<html lang=""zh-Hant"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>Claude Code WPF Web Session</title>
  <style>
    body{margin:0;background:#111;color:#eee;font-family:Segoe UI,Arial,sans-serif}
    header{height:42px;display:flex;align-items:center;gap:12px;padding:0 14px;background:#1f1f1f;border-bottom:1px solid #333}
    #meta{color:#aaa;font-size:12px}
    #messages{height:calc(100vh - 112px);overflow:auto;padding:18px}
    .msg{max-width:900px;margin:0 0 12px;padding:10px 12px;border-radius:8px;white-space:pre-wrap;line-height:1.45}
    .user{margin-left:auto;background:#0f4f8a}
    .assistant{background:#252525;border:1px solid #333}
    .system{background:#3a2714;color:#ffd49a}
    .tool{font-size:12px;background:#172a17;color:#a6e3a1}
    footer{height:69px;display:flex;gap:8px;padding:10px;background:#1f1f1f;border-top:1px solid #333}
    textarea{flex:1;resize:none;background:#151515;color:#eee;border:1px solid #444;border-radius:6px;padding:8px;font:14px Consolas,monospace}
    button{background:#ff7a18;color:white;border:0;border-radius:6px;padding:0 14px;cursor:pointer}
    button.secondary{background:#5b1f1f}
    button:disabled{opacity:.5;cursor:not-allowed}
  </style>
</head>
<body>
  <header>
    <strong>Open Claude Code WPF</strong>
    <span id=""meta"">Loading...</span>
    <button class=""secondary"" onclick=""cancelTurn()"">取消</button>
    <a href=""/logout"" style=""margin-left:auto;font-size:12px;color:#888;text-decoration:none"" title=""登出"">登出</a>
  </header>
  <main id=""messages""></main>
  <footer>
    <textarea id=""input"" placeholder=""輸入訊息，Enter 送出，Shift+Enter 換行""></textarea>
    <button id=""send"" onclick=""sendMessage()"">送出</button>
  </footer>
<script>
const token = new URLSearchParams(location.search).get('token') || '';
const base = location.pathname.replace(/\/$/, '');
const qs = token ? '?token=' + encodeURIComponent(token) : '';
const messages = document.getElementById('messages');
const input = document.getElementById('input');
const sendBtn = document.getElementById('send');
let currentAssistant = null;

function add(role, text) {
  const div = document.createElement('div');
  div.className = 'msg ' + role;
  div.textContent = text || '';
  messages.appendChild(div);
  messages.scrollTop = messages.scrollHeight;
  return div;
}

async function loadState() {
  const res = await fetch(base + '/api/state' + qs);
  const state = await res.json();
  document.getElementById('meta').textContent =
    state.title + ' · ' + state.provider + ' / ' + state.model;
  messages.innerHTML = '';
  (state.messages || []).forEach(m => {
    if (m.role === 'tool') add('tool', '[tool] ' + (m.name || '') + '\n' + (m.content || ''));
    else add(m.role === 'user' ? 'user' : 'assistant', m.content || '');
  });
  sendBtn.disabled = !!state.isRunning;
}

function connectEvents() {
  const es = new EventSource(base + '/events' + qs);
  es.addEventListener('user_message', e => add('user', JSON.parse(e.data).content));
  es.addEventListener('message_start', () => { currentAssistant = add('assistant', ''); sendBtn.disabled = true; });
  es.addEventListener('text_delta', e => {
    const data = JSON.parse(e.data);
    if (!currentAssistant) currentAssistant = add('assistant', '');
    currentAssistant.textContent += data.text || '';
    messages.scrollTop = messages.scrollHeight;
  });
  es.addEventListener('thinking_delta', e => {
    const data = JSON.parse(e.data);
    add('system', 'thinking: ' + (data.text || ''));
  });
  es.addEventListener('tool_started', e => {
    const data = JSON.parse(e.data);
    add('tool', '開始工具: ' + data.name + '\n' + data.input);
  });
  es.addEventListener('tool_completed', e => {
    const data = JSON.parse(e.data);
    add('tool', '工具完成: ' + data.name + '\n' + data.result);
  });
  es.addEventListener('tool_failed', e => {
    const data = JSON.parse(e.data);
    add('system', '工具失敗: ' + data.name + '\n' + data.error);
  });
  es.addEventListener('message_end', e => {
    const data = JSON.parse(e.data);
    if (data.isFinalTurn) sendBtn.disabled = false;
    currentAssistant = null;
  });
  es.addEventListener('cancelled', () => { add('system', '已取消'); sendBtn.disabled = false; currentAssistant = null; });
  es.addEventListener('error', e => {
    if (e.data) add('system', JSON.parse(e.data).message || 'Error');
    sendBtn.disabled = false;
  });
}

async function sendMessage() {
  if (sendBtn.disabled) return;
  const message = input.value.trim();
  if (!message) return;
  input.value = '';
  sendBtn.disabled = true;
  const res = await fetch(base + '/api/send' + qs, {
    method:'POST',
    headers:{'Content-Type':'application/json; charset=utf-8'},
    body:JSON.stringify({message})
  });
  if (!res.ok) {
    const text = await res.text();
    add('system', text);
    sendBtn.disabled = false;
  }
}

async function cancelTurn() {
  await fetch(base + '/api/cancel' + qs, { method:'POST' });
}

input.addEventListener('keydown', e => {
  if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); sendMessage(); }
});

loadState().then(connectEvents).catch(err => add('system', err.message));
</script>
</body>
</html>";
        }

        private static string BuildLoginHtml(string next, string errorMessage)
        {
            var errorHtml = string.IsNullOrEmpty(errorMessage)
                ? ""
                : "<p class=\"error\">" + HtmlEncode(errorMessage) + "</p>";
            var nextHtml = HtmlEncode(next ?? "");
            return @"<!doctype html>
<html lang=""zh-Hant"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>登入 – Open Claude Code WPF</title>
  <style>
    *{box-sizing:border-box}
    body{margin:0;background:#111;color:#eee;font-family:Segoe UI,Arial,sans-serif;
         display:flex;align-items:center;justify-content:center;min-height:100vh}
    .card{background:#1f1f1f;border:1px solid #333;border-radius:10px;padding:36px 40px;width:340px}
    h1{margin:0 0 24px;font-size:18px;color:#ff7a18}
    label{display:block;font-size:12px;color:#aaa;margin-bottom:4px}
    input[type=text],input[type=password]{
      width:100%;padding:9px 10px;background:#151515;color:#eee;
      border:1px solid #444;border-radius:6px;font-size:14px;margin-bottom:16px}
    input:focus{outline:none;border-color:#ff7a18}
    button{width:100%;padding:10px;background:#ff7a18;color:#fff;border:0;
           border-radius:6px;font-size:14px;cursor:pointer;margin-top:4px}
    button:hover{background:#e06010}
    .error{color:#f88;font-size:13px;margin-bottom:12px}
  </style>
</head>
<body>
  <div class=""card"">
    <h1>🔐 Web Session 登入</h1>" + errorHtml + @"
    <form method=""POST"" action=""/login"">
      <input type=""hidden"" name=""next"" value=""" + nextHtml + @""">
      <label for=""u"">帳號</label>
      <input type=""text"" id=""u"" name=""username"" autocomplete=""username"" autofocus>
      <label for=""p"">密碼</label>
      <input type=""password"" id=""p"" name=""password"" autocomplete=""current-password"">
      <button type=""submit"">登入</button>
    </form>
  </div>
</body>
</html>";
        }

        private enum WebRouteKind
        {
            Page,
            State,
            Send,
            Cancel,
            Events,
            Login,
            Logout
        }

        private class WebRoute
        {
            public WebRoute(string sessionId, WebRouteKind kind)
            {
                SessionId = sessionId;
                Kind = kind;
            }

            public string SessionId { get; private set; }
            public WebRouteKind Kind { get; private set; }
        }
    }
}
