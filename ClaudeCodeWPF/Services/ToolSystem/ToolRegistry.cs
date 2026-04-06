using System;
using System.Collections.Generic;
using System.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services.ToolSystem.Tools;

namespace OpenClaudeCodeWPF.Services.ToolSystem
{
    /// <summary>工具註冊表 — 管理所有可用工具</summary>
    public class ToolRegistry
    {
        private static ToolRegistry _instance;
        public static ToolRegistry Instance => _instance ?? (_instance = new ToolRegistry());

        private readonly Dictionary<string, IToolExecutor> _tools = new Dictionary<string, IToolExecutor>(StringComparer.OrdinalIgnoreCase);

        private ToolRegistry()
        {
            RegisterDefaults();
        }

        private void RegisterDefaults()
        {
            Register(new BashTool());
            Register(new PowerShellTool());
            Register(new FileReadTool());
            Register(new FileWriteTool());
            Register(new FileEditTool());
            Register(new GrepTool());
            Register(new GlobTool());
            Register(new WebFetchTool());
            Register(new WebSearchTool());
            Register(new AgentTool());
            Register(new OpenEdgeTool());
        }

        public void Register(IToolExecutor tool)
        {
            _tools[tool.Name] = tool;
        }

        public IToolExecutor GetTool(string name)
        {
            if (_tools.TryGetValue(name, out var tool)) return tool;
            throw new Exception($"Tool not found: {name}");
        }

        public bool TryGetTool(string name, out IToolExecutor tool)
        {
            return _tools.TryGetValue(name, out tool);
        }

        public List<IToolExecutor> GetAllTools() => _tools.Values.ToList();

        public List<ToolDefinition> GetAllToolDefinitions()
        {
            return _tools.Values.Select(t => t.ToDefinition()).ToList();
        }
    }
}
