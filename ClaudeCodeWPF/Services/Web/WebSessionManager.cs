using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services.Web
{
    public class WebSessionManager
    {
        private static WebSessionManager _instance;
        public static WebSessionManager Instance => _instance ?? (_instance = new WebSessionManager());

        private readonly object _lock = new object();
        private readonly Dictionary<string, WebSessionState> _sessions =
            new Dictionary<string, WebSessionState>(StringComparer.OrdinalIgnoreCase);

        private WebSessionManager()
        {
        }

        public string OpenSessionWindow(string sessionId)
        {
            if (!ConfigService.Instance.WebHostEnabled)
                throw new InvalidOperationException("Web Session 功能已在 App.config 停用。");

            var session = ConversationManager.Instance.FindSession(sessionId);
            if (session == null)
                throw new InvalidOperationException("找不到指定的對話 session。");

            var state = EnableSession(session);
            WebHostService.Instance.EnsureStarted();

            var url = BuildBrowserUrl(state);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return url;
        }

        public WebSessionState EnableSession(ConversationSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            lock (_lock)
            {
                if (!_sessions.TryGetValue(session.Id, out var state))
                {
                    state = new WebSessionState
                    {
                        SessionId = session.Id,
                        Token = GenerateToken(),
                        CreatedAt = DateTime.UtcNow
                    };
                    _sessions[session.Id] = state;
                }
                state.LastOpenedAt = DateTime.UtcNow;
                return state;
            }
        }

        public bool ValidateToken(string sessionId, string token)
        {
            if (!ConfigService.Instance.WebHostRequireToken)
                return true;

            lock (_lock)
            {
                if (!_sessions.TryGetValue(sessionId, out var state))
                    return false;
                return string.Equals(state.Token, token, StringComparison.Ordinal);
            }
        }

        public bool IsEnabled(string sessionId)
        {
            lock (_lock)
            {
                return _sessions.ContainsKey(sessionId);
            }
        }

        public string BuildBrowserUrl(WebSessionState state)
        {
            var host = ConfigService.Instance.WebHostHost;
            if (string.IsNullOrWhiteSpace(host) ||
                host == "0.0.0.0" ||
                host == "+" ||
                host == "*")
            {
                host = "localhost";
            }

            var url = "http://" + host + ":" + ConfigService.Instance.WebHostPort
                      + "/sessions/" + Uri.EscapeDataString(state.SessionId) + "/";

            if (ConfigService.Instance.WebHostRequireToken)
                url += "?token=" + Uri.EscapeDataString(state.Token);

            return url;
        }

        private static string GenerateToken()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }

    public class WebSessionState
    {
        public string SessionId { get; set; }
        public string Token { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastOpenedAt { get; set; }
    }
}
