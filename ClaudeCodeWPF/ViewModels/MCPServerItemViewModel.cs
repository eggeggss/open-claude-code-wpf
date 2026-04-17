using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using OpenClaudeCodeWPF.Services.MCP;

namespace OpenClaudeCodeWPF.ViewModels
{
    public enum McpServerStatus { Disconnected, Connecting, Connected, Error }

    public class MCPServerItemViewModel : ViewModelBase
    {
        // ─── Backing fields ───────────────────────────────────────────
        private string _name;
        private McpServerType _type = McpServerType.Stdio;
        private string _command;
        private string _envVarsText;   // KEY=VALUE per line
        private string _tools = "*";
        private string _url;           // for HTTP / SSE
        private bool _enabled = true;
        private McpServerStatus _status = McpServerStatus.Disconnected;
        private bool _isEditing;

        // ─── Properties ───────────────────────────────────────────────
        public string Name
        {
            get => _name;
            set { Set(ref _name, value); }
        }

        public McpServerType Type
        {
            get => _type;
            set
            {
                Set(ref _type, value);
                OnPropertyChanged(nameof(IsProcessBased));
                OnPropertyChanged(nameof(IsUrlBased));
            }
        }

        public string Command
        {
            get => _command;
            set => Set(ref _command, value);
        }

        public string EnvVarsText
        {
            get => _envVarsText;
            set => Set(ref _envVarsText, value);
        }

        public string Tools
        {
            get => _tools;
            set => Set(ref _tools, value);
        }

        public string Url
        {
            get => _url;
            set => Set(ref _url, value);
        }

        public bool Enabled
        {
            get => _enabled;
            set => Set(ref _enabled, value);
        }

        public McpServerStatus Status
        {
            get => _status;
            set
            {
                Set(ref _status, value);
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(IsConnected));
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            set => Set(ref _isEditing, value);
        }

        // ─── Derived ──────────────────────────────────────────────────
        public bool IsProcessBased => Type == McpServerType.Local || Type == McpServerType.Stdio;
        public bool IsUrlBased => Type == McpServerType.Http || Type == McpServerType.Sse;
        public bool IsConnected => Status == McpServerStatus.Connected;

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case McpServerStatus.Connected:  return "✔ 已連接";
                    case McpServerStatus.Connecting: return "… 連接中";
                    case McpServerStatus.Error:      return "✖ 錯誤";
                    default:                         return "○ 未連接";
                }
            }
        }

        public Brush StatusColor
        {
            get
            {
                switch (Status)
                {
                    case McpServerStatus.Connected:  return new SolidColorBrush(Color.FromRgb(0x4E, 0xC9, 0x4E));
                    case McpServerStatus.Connecting: return new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00));
                    case McpServerStatus.Error:      return new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                    default:                         return new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                }
            }
        }

        public string TypeLabel => Type.ToString();

        // ─── Conversion ───────────────────────────────────────────────
        public MCPServerConfig ToConfig()
        {
            var cfg = new MCPServerConfig
            {
                Name = Name?.Trim(),
                Type = Type,
                Enabled = Enabled,
                Tools = string.IsNullOrWhiteSpace(Tools) ? "*" : Tools.Trim(),
                Env = ParseEnvVars(),
                Transport = (Type == McpServerType.Sse) ? "sse" : "stdio",
                SseUrl = Url?.Trim()
            };

            // Parse command into exe + args
            var parts = SplitCommandLine(Command?.Trim() ?? "");
            if (parts.Count > 0)
            {
                cfg.Command = parts[0];
                cfg.Args = parts.Skip(1).ToList();
            }

            return cfg;
        }

        public static MCPServerItemViewModel FromConfig(MCPServerConfig cfg)
        {
            var vm = new MCPServerItemViewModel
            {
                Name = cfg.Name,
                Type = cfg.Type,
                Enabled = cfg.Enabled,
                Tools = cfg.Tools ?? "*",
                Url = cfg.SseUrl,
                EnvVarsText = cfg.Env != null
                    ? string.Join(Environment.NewLine, cfg.Env.Select(kv => $"{kv.Key}={kv.Value}"))
                    : ""
            };

            // Re-assemble command line
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(cfg.Command)) parts.Add(cfg.Command);
            if (cfg.Args != null) parts.AddRange(cfg.Args);
            vm.Command = string.Join(" ", parts);

            return vm;
        }

        // ─── Helpers ──────────────────────────────────────────────────
        private Dictionary<string, string> ParseEnvVars()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(EnvVarsText)) return result;

            foreach (var line in EnvVarsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return result;
        }

        private static List<string> SplitCommandLine(string cmd)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(cmd)) return result;

            bool inQuotes = false;
            var current = new System.Text.StringBuilder();
            foreach (char c in cmd)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }
    }
}
