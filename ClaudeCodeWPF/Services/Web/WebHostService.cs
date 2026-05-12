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
  <title>Open Claude Code WPF</title>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0 }
    :root {
      --bg: #0d0d10; --surface: #141418; --surface2: #1c1c24; --surface3: #252530;
      --border: #2e2e3c; --accent: #ff7a18; --accent-dim: #c25a0e;
      --text: #e2e2ea; --text-dim: #9090a8; --text-muted: #55556a;
      --user-bg: #0e2f4e; --user-border: #1a4a70;
      --asst-bg: #1a1a22; --code-bg: #0a0a12; --code-border: #252535;
      --tool-bg: #0a130a; --tool-color: #6eca6e;
      --sys-bg: #1a160a; --sys-color: #d4b060;
      --green: #4ecb80; --red: #e06060;
    }
    html, body { height: 100%; }
    body { background: var(--bg); color: var(--text); font-family: 'Segoe UI', system-ui, -apple-system, sans-serif; display: flex; flex-direction: column; overflow: hidden; }
    ::-webkit-scrollbar { width: 5px } ::-webkit-scrollbar-track { background: transparent }
    ::-webkit-scrollbar-thumb { background: var(--border); border-radius: 3px }
    ::-webkit-scrollbar-thumb:hover { background: var(--text-muted) }

    /* Header */
    #header { height: 52px; min-height: 52px; display: flex; align-items: center; gap: 10px; padding: 0 20px; background: var(--surface); border-bottom: 1px solid var(--border); z-index: 10; }
    #app-logo { width: 28px; height: 28px; background: var(--accent); border-radius: 7px; display: flex; align-items: center; justify-content: center; font-size: 14px; flex-shrink: 0; }
    #app-name { font-weight: 700; font-size: 14px; color: var(--text); letter-spacing: 0.2px; }
    #status-dot { width: 7px; height: 7px; border-radius: 50%; background: var(--text-muted); flex-shrink: 0; transition: background 0.3s, box-shadow 0.3s; }
    #status-dot.live { background: var(--green); box-shadow: 0 0 7px var(--green); }
    #status-dot.busy { background: var(--accent); animation: pulse 1s infinite; }
    @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.35} }
    #session-info { font-size: 12px; color: var(--text-dim); margin-right: auto; }
    .hdr-btn { font-size: 12px; color: var(--text-dim); background: none; border: 1px solid var(--border); border-radius: 6px; padding: 4px 11px; cursor: pointer; transition: all 0.15s; text-decoration: none; display: inline-flex; align-items: center; gap: 5px; }
    .hdr-btn:hover { color: var(--text); border-color: var(--text-muted); }
    .hdr-btn:disabled { opacity: .4; cursor: not-allowed; }
    .hdr-btn.danger:hover { color: var(--red); border-color: var(--red); }

    /* Messages */
    #messages { flex: 1; overflow-y: auto; overflow-x: hidden; padding: 28px 0 12px; }
    .msg-wrap { max-width: 860px; margin: 0 auto; padding: 0 24px; animation: fadeUp 0.2s ease; }
    @keyframes fadeUp { from { opacity: 0; transform: translateY(8px) } to { opacity: 1; transform: none } }
    .msg-row { display: flex; gap: 12px; align-items: flex-start; margin-bottom: 20px; }
    .msg-row.user { flex-direction: row-reverse; }
    .msg-avatar { width: 32px; height: 32px; border-radius: 8px; flex-shrink: 0; display: flex; align-items: center; justify-content: center; font-size: 15px; line-height: 1; }
    .user .msg-avatar { background: var(--user-bg); border: 1px solid var(--user-border); }
    .asst .msg-avatar { background: var(--surface3); border: 1px solid var(--border); }
    .msg-body { flex: 1; min-width: 0; }
    .msg-role { font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.6px; margin-bottom: 5px; color: var(--text-muted); }
    .user .msg-role { text-align: right; color: #4a90c8; }
    .asst .msg-role { color: var(--accent); }
    .msg-bubble { border-radius: 12px; font-size: 14px; line-height: 1.65; word-wrap: break-word; }
    .user .msg-bubble { background: var(--user-bg); border: 1px solid var(--user-border); border-radius: 12px 4px 12px 12px; padding: 12px 16px; white-space: pre-wrap; color: #c8e0f4; }
    .asst .msg-bubble { background: var(--asst-bg); border: 1px solid var(--border); border-radius: 4px 12px 12px 12px; padding: 14px 18px; }
    .sys-bubble { background: var(--sys-bg); border: 1px solid #2e2810; border-radius: 8px; padding: 10px 14px; font-size: 13px; color: var(--sys-color); white-space: pre-wrap; margin: 0 0 12px; max-width: 860px; margin: 0 auto 12px; }
    .tool-bubble { background: var(--tool-bg); border: 1px solid #152215; border-radius: 8px; padding: 0; font-size: 12px; color: var(--tool-color); margin: 0 0 20px; max-width: 860px; margin: 0 auto 20px; font-family: Consolas, 'Courier New', monospace; overflow: hidden; }
    .tool-bubble summary { cursor: pointer; user-select: none; padding: 9px 14px; display: flex; align-items: center; gap: 8px; border-radius: 8px; list-style: none; }
    .tool-bubble summary::-webkit-details-marker { display: none }
    .tool-bubble summary::before { content: '▶'; font-size: 9px; opacity: .6; transition: transform 0.18s; }
    .tool-bubble[open] summary::before { transform: rotate(90deg); }
    .tool-bubble summary:hover { background: rgba(255,255,255,.04); }
    .tool-icon { font-size: 13px; }
    .tool-name { font-weight: 600; font-size: 12px; }
    .tool-body { padding: 0 14px 12px; border-top: 1px solid #1e321e; margin-top: -1px; }
    .tool-section { margin-top: 8px; }
    .tool-label { font-size: 10px; font-weight: 700; letter-spacing: 0.5px; color: var(--text-muted); margin-bottom: 4px; }
    .tool-content { white-space: pre-wrap; opacity: .85; }

    /* Markdown */
    .md h1,.md h2,.md h3 { font-weight: 600; color: var(--text); margin: 16px 0 8px; line-height: 1.3; }
    .md h1 { font-size: 18px } .md h2 { font-size: 16px } .md h3 { font-size: 15px }
    .md p { margin: 0 0 10px } .md p:last-child { margin: 0 }
    .md ul,.md ol { padding-left: 22px; margin: 0 0 10px } .md li { margin-bottom: 4px }
    .md code { font-family: Consolas, 'Courier New', monospace; background: var(--code-bg); border: 1px solid var(--code-border); padding: 1px 6px; border-radius: 4px; font-size: 13px; }
    .md pre { background: var(--code-bg); border: 1px solid var(--code-border); border-radius: 8px; margin: 12px 0; position: relative; overflow: hidden; }
    .md pre code { background: none; border: none; padding: 0; color: #c0cce0; font-size: 13px; line-height: 1.55; display: block; overflow-x: auto; white-space: pre; padding: 14px 16px; }
    .md pre .lang-tag { position: absolute; top: 0; left: 0; font-size: 10px; padding: 3px 8px; background: rgba(255,255,255,.06); color: var(--text-muted); border-radius: 0 0 5px 0; font-family: Consolas, monospace; text-transform: uppercase; }
    .md .copy-btn { position: absolute; top: 6px; right: 8px; font-size: 11px; padding: 3px 9px; background: var(--surface3); border: 1px solid var(--border); border-radius: 5px; color: var(--text-dim); cursor: pointer; opacity: 0; transition: opacity 0.15s; }
    .md pre:hover .copy-btn { opacity: 1; }
    .md .copy-btn:hover { background: var(--border); color: var(--text); }
    .md .copy-btn.ok { color: var(--green); border-color: var(--green); }
    .md .copy-btn.err { color: var(--red); border-color: var(--red); opacity: 1; }
    .md strong { font-weight: 600 } .md em { color: var(--text-dim) } .md del { opacity: .6 }
    .md blockquote { border-left: 3px solid var(--accent-dim); padding: 6px 14px; margin: 10px 0; color: var(--text-dim); background: rgba(255,122,24,.05); border-radius: 0 6px 6px 0; }
    .md a { color: #5aafe0; text-decoration: none } .md a:hover { text-decoration: underline }
    .md hr { border: none; border-top: 1px solid var(--border); margin: 14px 0 }
    .md table { width: 100%; border-collapse: collapse; font-size: 13px; margin: 10px 0 }
    .md th,.md td { padding: 7px 12px; border: 1px solid var(--border); text-align: left }
    .md th { background: var(--surface3); font-weight: 600 }
    .md tr:nth-child(even) td { background: rgba(255,255,255,.025) }

    /* Thinking */
    .thinking-block { background: var(--surface2); border: 1px solid var(--border); border-radius: 8px; margin-bottom: 10px; font-size: 12px; overflow: hidden; }
    .thinking-block summary { cursor: pointer; padding: 7px 12px; color: var(--text-muted); font-style: italic; list-style: none; }
    .thinking-block summary::-webkit-details-marker { display: none }
    .thinking-block summary::before { content: '💭 '; }
    .thinking-body { padding: 0 12px 10px; white-space: pre-wrap; color: var(--text-muted); font-size: 12px; }
    .cursor { display: inline-block; width: 2px; height: 14px; background: var(--accent); margin-left: 2px; vertical-align: middle; animation: blink .75s infinite; }
    @keyframes blink { 0%,100%{opacity:1} 50%{opacity:0} }

    /* Footer */
    #footer { background: var(--surface); border-top: 1px solid var(--border); padding: 14px 24px; }
    #input-row { max-width: 860px; margin: 0 auto; display: flex; gap: 10px; align-items: flex-end; }
    #input { flex: 1; background: var(--surface2); color: var(--text); border: 1px solid var(--border); border-radius: 10px; padding: 10px 14px; font: 14px 'Segoe UI', system-ui, sans-serif; line-height: 1.55; min-height: 44px; max-height: 200px; resize: none; outline: none; transition: border-color 0.2s; overflow-y: auto; }
    #input:focus { border-color: var(--accent); }
    #input::placeholder { color: var(--text-muted); }
    #send-btn { width: 44px; height: 44px; border-radius: 10px; flex-shrink: 0; background: var(--accent); border: none; color: white; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all 0.15s; }
    #send-btn:hover:not(:disabled) { background: var(--accent-dim); transform: scale(1.06); }
    #send-btn:disabled { opacity: .35; cursor: not-allowed; transform: none; }
  </style>
</head>
<body>
  <div id=""header"">
    <div id=""app-logo"">⚡</div>
    <span id=""app-name"">Open Claude Code WPF</span>
    <div id=""status-dot"" title=""連線狀態""></div>
    <span id=""session-info"">載入中…</span>
    <button class=""hdr-btn danger"" onclick=""cancelTurn()"" id=""cancel-btn"" disabled>⏹ 取消</button>
    <a href=""/logout"" class=""hdr-btn"">🔓 登出</a>
  </div>
  <div id=""messages""></div>
  <div id=""footer"">
    <div id=""input-row"">
      <textarea id=""input"" rows=""1"" placeholder=""輸入訊息… (Enter 送出，Shift+Enter 換行)""></textarea>
      <button id=""send-btn"" onclick=""sendMessage()"" title=""送出"">
        <svg width=""18"" height=""18"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2.5"" stroke-linecap=""round"" stroke-linejoin=""round""><line x1=""22"" y1=""2"" x2=""11"" y2=""13""/><polygon points=""22 2 15 22 11 13 2 9 22 2""/></svg>
      </button>
    </div>
  </div>
<script>
const token = new URLSearchParams(location.search).get('token') || '';
const base = location.pathname.replace(/\/$/, '');
const qs = token ? '?token=' + encodeURIComponent(token) : '';
const msgsEl = document.getElementById('messages');
const inputEl = document.getElementById('input');
const sendBtn = document.getElementById('send-btn');
const cancelBtn = document.getElementById('cancel-btn');
const statusDot = document.getElementById('status-dot');
let curAsst = null, curThinkBody = null, rawText = '', streamCursor = null, refreshTimer = null;

// ── Markdown ──────────────────────────────────────────────────────────
function esc(s){ return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
function inline(s){
  return esc(s)
    .replace(/(^|[^`])`([^`\n]+)`(?!`)/g,'$1<code>$2</code>')
    .replace(/\*\*\*(.+?)\*\*\*/g,'<strong><em>$1</em></strong>')
    .replace(/\*\*(.+?)\*\*/g,'<strong>$1</strong>')
    .replace(/\*(.+?)\*/g,'<em>$1</em>')
    .replace(/~~(.+?)~~/g,'<del>$1</del>')
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g,'<a href=""$2"" target=""_blank"" rel=""noopener"">$1</a>');
}
function normalizeFences(text){
  text = String(text || '').replace(/\r\n?/g, '\n');
  let out = '', i = 0, inFence = false, lineHasText = false;
  while(i < text.length){
    if(text.slice(i, i + 3) === '```'){
      if(!inFence && lineHasText) out += '\n';
      out += '```';
      i += 3;
      inFence = !inFence;
      lineHasText = true;
      if(!inFence && i < text.length && text[i] !== '\n') {
        out += '\n';
        lineHasText = false;
      }
      continue;
    }
    const ch = text[i++];
    out += ch;
    if(ch === '\n') lineHasText = false;
    else if(!/\s/.test(ch)) lineHasText = true;
  }
  return out;
}
function codeBlockHtml(lang, text){
  const id='c'+Math.random().toString(36).slice(2);
  const lt=lang?`<span class=""lang-tag"">${esc(lang)}</span>`:'';
  return `<pre>${lt}<button type=""button"" class=""copy-btn"" data-id=""${id}"">複製</button><code id=""${id}"">${esc(text)}</code></pre>`;
}
function md(text){
  if(!text) return '';
  text = normalizeFences(text);
  const lines = text.split('\n'); let out='', inFence=false, lang='', fence=[], inUl=false, inOl=false, inTable=false, tableHead=false;
  function flushLists(){ if(inUl){out+='</ul>';inUl=false;} if(inOl){out+='</ol>';inOl=false;} }
  function flushTable(){ if(inTable){out+='</tbody></table>';inTable=false;tableHead=false;} }
  for(let i=0;i<lines.length;i++){
    const L=lines[i], T=L.trim();
    if(!inFence && T.startsWith('```')){ flushLists();flushTable(); inFence=true; lang=T.slice(3).trim(); fence=[]; continue; }
    if(inFence){ if(T.startsWith('```')){ inFence=false; out+=codeBlockHtml(lang, fence.join('\n')); lang=''; }else{ fence.push(L); } continue; }
    const hm=T.match(/^(#{1,3}) (.+)/); if(hm){flushLists();flushTable();out+=`<h${hm[1].length}>${inline(hm[2])}</h${hm[1].length}>`;continue;}
    if(/^---+$|^\*\*\*+$/.test(T)){flushLists();flushTable();out+='<hr>';continue;}
    if(T.startsWith('> ')){flushLists();flushTable();out+=`<blockquote>${inline(T.slice(2))}</blockquote>`;continue;}
    if(T.includes('|') && T.startsWith('|')){
      flushLists();
      const cells=T.split('|').slice(1,-1).map(c=>c.trim());
      if(!inTable){ out+='<table><thead><tr>'+cells.map(c=>`<th>${inline(c)}</th>`).join('')+'</tr></thead><tbody>'; inTable=true; tableHead=true; continue; }
      if(tableHead && cells.every(c=>/^[-:]+$/.test(c))){ tableHead=false; continue; }
      out+='<tr>'+cells.map(c=>`<td>${inline(c)}</td>`).join('')+'</tr>'; continue;
    }
    flushTable();
    const ul=L.match(/^(\s*)[-*+] (.+)/); if(ul){if(inOl){out+='</ol>';inOl=false;}if(!inUl){out+='<ul>';inUl=true;}out+=`<li>${inline(ul[2])}</li>`;continue;}
    const ol=L.match(/^(\s*)\d+\. (.+)/); if(ol){if(inUl){out+='</ul>';inUl=false;}if(!inOl){out+='<ol>';inOl=true;}out+=`<li>${inline(ol[2])}</li>`;continue;}
    flushLists();
    if(T===''){out+='<p></p>';continue;}
    out+=`<p>${inline(L)}</p>`;
  }
  flushLists(); flushTable();
  if(inFence) out+=codeBlockHtml(lang, fence.join('\n'));
  return out;
}
async function copyText(text){
  if(!text) return false;
  if(navigator.clipboard && window.isSecureContext){
    try { await navigator.clipboard.writeText(text); return true; } catch { }
  }
  const ta=document.createElement('textarea');
  ta.value=text; ta.setAttribute('readonly','');
  ta.style.position='fixed'; ta.style.left='-9999px'; ta.style.top='0';
  document.body.appendChild(ta);
  ta.focus(); ta.select();
  try { return document.execCommand('copy'); }
  catch { return false; }
  finally { document.body.removeChild(ta); }
}
document.addEventListener('click', async e => {
  const btn = e.target.closest('.copy-btn');
  if(!btn) return;
  const code = (btn.dataset.id && document.getElementById(btn.dataset.id)) || btn.closest('pre')?.querySelector('code');
  const ok = await copyText(code?.textContent||'');
  btn.textContent=ok?'✓':'失敗'; btn.classList.toggle('ok',ok); btn.classList.toggle('err',!ok);
  setTimeout(()=>{btn.textContent='複製';btn.classList.remove('ok','err');}, 2000);
});

// ── Add message ───────────────────────────────────────────────────────
function addMsg(role, content, toolName){
  if(role==='system'){
    const d=document.createElement('div'); d.className='msg-wrap';
    const b=document.createElement('div'); b.className='sys-bubble';
    b.textContent=content||''; d.appendChild(b); msgsEl.appendChild(d); scrollBot(); return {bubble:b};
  }
  if(role==='tool'){
    const d=document.createElement('div'); d.className='msg-wrap';
    const det=document.createElement('details'); det.className='tool-bubble';
    const sum=document.createElement('summary');
    sum.innerHTML=`<span class=""tool-icon"">🔧</span><span class=""tool-name"">${esc(toolName||'Tool')}</span>`;
    const body=document.createElement('div'); body.className='tool-body';
    det.appendChild(sum); det.appendChild(body); d.appendChild(det); msgsEl.appendChild(d); scrollBot();
    return {bubble:body};
  }
  const wrap=document.createElement('div'); wrap.className='msg-wrap';
  const row=document.createElement('div'); row.className='msg-row '+(role==='user'?'user':'asst');
  const av=document.createElement('div'); av.className='msg-avatar'; av.textContent=role==='user'?'👤':'⚡';
  const body=document.createElement('div'); body.className='msg-body';
  const rl=document.createElement('div'); rl.className='msg-role'; rl.textContent=role==='user'?'使用者':'助理';
  const bub=document.createElement('div'); bub.className='msg-bubble'+(role==='asst'?' md':'');
  if(role==='asst') bub.innerHTML=md(content||''); else bub.textContent=content||'';
  body.appendChild(rl); body.appendChild(bub);
  row.appendChild(av); row.appendChild(body); wrap.appendChild(row); msgsEl.appendChild(wrap); scrollBot();
  return {bubble:bub};
}
function scrollBot(){ msgsEl.scrollTop=msgsEl.scrollHeight; }

// ── Status ────────────────────────────────────────────────────────────
function setRunning(on){
  sendBtn.disabled=on; cancelBtn.disabled=!on;
  statusDot.className=''; statusDot.classList.add(on?'busy':'live');
}

// ── Load state ────────────────────────────────────────────────────────
async function loadState(){
  const r=await fetch(base+'/api/state'+qs), s=await r.json();
  document.getElementById('session-info').textContent=(s.title||'對話')+'  ·  '+s.provider+' / '+s.model;
  msgsEl.innerHTML='';
  (s.messages||[]).forEach(m=>{
    if(m.role==='tool') addMsg('tool','[輸入]\n'+(m.input||'')+'\n\n[輸出]\n'+(m.content||''), m.name||'');
    else addMsg(m.role==='user'?'user':'asst', m.content||'');
  });
  setRunning(!!s.isRunning);
  statusDot.classList.add('live');
  return s;
}
function scheduleStateRefresh(delay, attempt){
  if(refreshTimer) clearTimeout(refreshTimer);
  refreshTimer=setTimeout(async()=>{
    refreshTimer=null;
    try {
      const s=await loadState();
      const n=attempt||0;
      if(n<4 && (s.isRunning || n===0)) scheduleStateRefresh(s.isRunning?500:900, n+1);
    } catch(err) {
      addMsg('system','重新整理 Web 狀態失敗: '+(err.message||err));
      setRunning(false);
    }
  }, delay||350);
}

// ── SSE ───────────────────────────────────────────────────────────────
function connectEvents(){
  const es=new EventSource(base+'/events'+qs);
  es.addEventListener('user_message', e=>addMsg('user', JSON.parse(e.data).content));
  es.addEventListener('message_start', ()=>{
    if(refreshTimer){clearTimeout(refreshTimer);refreshTimer=null;}
    setRunning(true); rawText=''; curThinkBody=null;
    const {bubble}=addMsg('asst',''); curAsst=bubble; curAsst.innerHTML='';
    streamCursor=document.createElement('span'); streamCursor.className='cursor'; curAsst.appendChild(streamCursor);
  });
  es.addEventListener('thinking_delta', e=>{
    if(!curAsst) return;
    const {text}=JSON.parse(e.data);
    if(!curThinkBody){
      const det=document.createElement('details'); det.className='thinking-block';
      const sum=document.createElement('summary'); sum.textContent='思考中…';
      const div=document.createElement('div'); div.className='thinking-body';
      det.appendChild(sum); det.appendChild(div);
      if(streamCursor) curAsst.insertBefore(det,streamCursor); else curAsst.appendChild(det);
      curThinkBody=div;
    }
    curThinkBody.textContent+=text; scrollBot();
  });
  es.addEventListener('text_delta', e=>{
    const {text}=JSON.parse(e.data);
    if(!curAsst){ const {bubble}=addMsg('asst',''); curAsst=bubble; curAsst.innerHTML=''; rawText=''; streamCursor=document.createElement('span'); streamCursor.className='cursor'; curAsst.appendChild(streamCursor); }
    rawText+=text;
    if(streamCursor) streamCursor.remove();
    curAsst.innerHTML=md(rawText);
    streamCursor=document.createElement('span'); streamCursor.className='cursor'; curAsst.appendChild(streamCursor);
    scrollBot();
  });
  es.addEventListener('tool_started', e=>{
    const {name,input}=JSON.parse(e.data);
    const {bubble}=addMsg('tool','',name);
    const inp=document.createElement('div'); inp.className='tool-section';
    inp.innerHTML=`<div class=""tool-label"">INPUT</div><div class=""tool-content"">${esc(typeof input==='string'?input:JSON.stringify(input,null,2))}</div>`;
    bubble.appendChild(inp);
  });
  es.addEventListener('tool_completed', e=>{
    const {name,result}=JSON.parse(e.data);
    const {bubble}=addMsg('tool','',name);
    const out=document.createElement('div'); out.className='tool-section';
    out.innerHTML=`<div class=""tool-label"">OUTPUT</div><div class=""tool-content"">${esc(result||'')}</div>`;
    bubble.appendChild(out);
  });
  es.addEventListener('tool_failed', e=>{ const {name,error}=JSON.parse(e.data); addMsg('system',`工具 ""${name}"" 失敗:\n${error}`); });
  es.addEventListener('message_end', e=>{
    const {isFinalTurn}=JSON.parse(e.data);
    if(streamCursor){streamCursor.remove();streamCursor=null;}
    curAsst=null; curThinkBody=null; rawText='';
    if(isFinalTurn){ setRunning(false); scheduleStateRefresh(350, 0); } scrollBot();
  });
  es.addEventListener('cancelled', ()=>{
    if(streamCursor){streamCursor.remove();streamCursor=null;}
    curAsst=null; curThinkBody=null; rawText='';
    addMsg('system','已取消'); setRunning(false);
  });
  es.addEventListener('error', e=>{ if(e.data) addMsg('system',JSON.parse(e.data).message||'發生錯誤'); setRunning(false); });
}

// ── Send / Cancel ─────────────────────────────────────────────────────
async function sendMessage(){
  if(sendBtn.disabled) return;
  const msg=inputEl.value.trim(); if(!msg) return;
  inputEl.value=''; inputEl.style.height=''; setRunning(true);
  const res=await fetch(base+'/api/send'+qs,{method:'POST',headers:{'Content-Type':'application/json; charset=utf-8'},body:JSON.stringify({message:msg})});
  if(!res.ok){ addMsg('system',await res.text()); setRunning(false); }
}
async function cancelTurn(){ await fetch(base+'/api/cancel'+qs,{method:'POST'}); }

// Auto-resize textarea
inputEl.addEventListener('input',()=>{ inputEl.style.height='auto'; inputEl.style.height=Math.min(inputEl.scrollHeight,200)+'px'; });
inputEl.addEventListener('keydown', e=>{ if(e.key==='Enter'&&!e.shiftKey&&!e.isComposing){e.preventDefault();sendMessage();} });

loadState().then(connectEvents).catch(err=>addMsg('system',err.message));
</script>
</body>
</html>";
        }

        private static string BuildLoginHtml(string next, string errorMessage)
        {
            var errorHtml = string.IsNullOrEmpty(errorMessage)
                ? ""
                : "<div class=\"err-banner\">⚠ " + HtmlEncode(errorMessage) + "</div>";
            var nextHtml = HtmlEncode(next ?? "");
            return @"<!doctype html>
<html lang=""zh-Hant"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>登入 – Open Claude Code WPF</title>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0 }
    :root { --bg:#0d0d10; --surface:#141418; --surface2:#1c1c24; --border:#2e2e3c; --accent:#ff7a18; --accent-dim:#c25a0e; --text:#e2e2ea; --text-dim:#9090a8; --text-muted:#55556a; }
    body { background: var(--bg); color: var(--text); font-family: 'Segoe UI', system-ui, -apple-system, sans-serif; min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px; }
    .card { background: var(--surface); border: 1px solid var(--border); border-radius: 16px; padding: 42px 44px; width: 100%; max-width: 380px; box-shadow: 0 20px 60px rgba(0,0,0,.5); }
    .logo-row { display: flex; align-items: center; gap: 10px; margin-bottom: 28px; }
    .logo { width: 36px; height: 36px; background: var(--accent); border-radius: 10px; display: flex; align-items: center; justify-content: center; font-size: 18px; flex-shrink: 0; }
    .logo-text { font-weight: 700; font-size: 15px; line-height: 1.2; }
    .logo-sub { font-size: 11px; color: var(--text-muted); }
    h1 { font-size: 14px; font-weight: 600; color: var(--text-dim); margin-bottom: 24px; letter-spacing: 0.3px; }
    .err-banner { background: rgba(200,60,60,.15); border: 1px solid rgba(200,60,60,.35); border-radius: 8px; padding: 10px 14px; font-size: 13px; color: #f08080; margin-bottom: 18px; }
    .field { margin-bottom: 18px; }
    label { display: block; font-size: 12px; font-weight: 600; color: var(--text-muted); margin-bottom: 6px; letter-spacing: 0.3px; text-transform: uppercase; }
    input[type=text], input[type=password] { width: 100%; padding: 11px 13px; background: var(--surface2); color: var(--text); border: 1px solid var(--border); border-radius: 9px; font-size: 14px; outline: none; transition: border-color 0.2s, box-shadow 0.2s; }
    input:focus { border-color: var(--accent); box-shadow: 0 0 0 3px rgba(255,122,24,.15); }
    input::placeholder { color: var(--text-muted); }
    button[type=submit] { width: 100%; padding: 12px; background: var(--accent); color: #fff; border: none; border-radius: 9px; font-size: 14px; font-weight: 600; cursor: pointer; margin-top: 6px; transition: background 0.15s, transform 0.1s; letter-spacing: 0.2px; }
    button[type=submit]:hover { background: var(--accent-dim); }
    button[type=submit]:active { transform: scale(.98); }
  </style>
</head>
<body>
  <div class=""card"">
    <div class=""logo-row"">
      <div class=""logo"">⚡</div>
      <div>
        <div class=""logo-text"">Open Claude Code WPF</div>
        <div class=""logo-sub"">Web Session</div>
      </div>
    </div>
    <h1>請輸入帳號密碼以繼續</h1>" + errorHtml + @"
    <form method=""POST"" action=""/login"">
      <input type=""hidden"" name=""next"" value=""" + nextHtml + @""">
      <div class=""field"">
        <label for=""u"">帳號</label>
        <input type=""text"" id=""u"" name=""username"" placeholder=""輸入帳號"" autocomplete=""username"" autofocus>
      </div>
      <div class=""field"">
        <label for=""p"">密碼</label>
        <input type=""password"" id=""p"" name=""password"" placeholder=""輸入密碼"" autocomplete=""current-password"">
      </div>
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
