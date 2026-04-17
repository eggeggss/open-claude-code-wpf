using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Services.MCP;

namespace OpenClaudeCodeWPF.Services
{
    /// <summary>讀寫 mcp-servers.json，提供 MCP 伺服器設定的 CRUD 操作</summary>
    public class MCPConfigService
    {
        private static MCPConfigService _instance;
        public static MCPConfigService Instance => _instance ?? (_instance = new MCPConfigService());

        private readonly string _filePath;

        private MCPConfigService()
        {
            var configuredPath = ConfigService.Instance.MCPConfigPath;
            // Support relative (to AppData) or absolute path
            if (Path.IsPathRooted(configuredPath))
                _filePath = configuredPath;
            else
            {
                var appData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClaudeCodeWPF");
                Directory.CreateDirectory(appData);
                _filePath = Path.Combine(appData, configuredPath);
            }
        }

        public List<MCPServerConfig> LoadAll()
        {
            if (!File.Exists(_filePath))
                return new List<MCPServerConfig>();

            try
            {
                var json = File.ReadAllText(_filePath);
                var obj = JObject.Parse(json);
                var arr = obj["servers"] as JArray;
                if (arr == null) return new List<MCPServerConfig>();
                return arr.ToObject<List<MCPServerConfig>>() ?? new List<MCPServerConfig>();
            }
            catch
            {
                return new List<MCPServerConfig>();
            }
        }

        public void SaveAll(List<MCPServerConfig> servers)
        {
            var obj = new JObject
            {
                ["servers"] = JArray.FromObject(servers)
            };
            File.WriteAllText(_filePath, obj.ToString(Formatting.Indented));
        }

        public void Add(MCPServerConfig server)
        {
            var list = LoadAll();
            list.Add(server);
            SaveAll(list);
        }

        public void Update(MCPServerConfig server)
        {
            var list = LoadAll();
            var idx = list.FindIndex(s => s.Name == server.Name);
            if (idx >= 0)
                list[idx] = server;
            else
                list.Add(server);
            SaveAll(list);
        }

        public void Delete(string serverName)
        {
            var list = LoadAll();
            list.RemoveAll(s => s.Name == serverName);
            SaveAll(list);
        }

        public void SetEnabled(string serverName, bool enabled)
        {
            var list = LoadAll();
            var s = list.Find(x => x.Name == serverName);
            if (s != null)
            {
                s.Enabled = enabled;
                SaveAll(list);
            }
        }

        public string FilePath => _filePath;
    }
}
