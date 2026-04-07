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
            Register(new ReadDocumentTool());
            // Browser automation (Chrome DevTools Protocol)
            Register(new BrowserConnectTool());
            Register(new BrowserNavigateTool());
            Register(new BrowserScreenshotTool());
            Register(new BrowserClickTool());
            Register(new BrowserFillTool());
            Register(new BrowserSelectTool());
            Register(new BrowserTypeTool());
            Register(new BrowserGetTextTool());
            Register(new BrowserFindElementsTool());
            Register(new BrowserScrollTool());
            Register(new BrowserExecuteJsTool());
            Register(new BrowserCloseTool());
            // Desktop automation (WinAppDriver)
            Register(new DesktopLaunchAppTool());
            Register(new DesktopAttachWindowTool());
            Register(new DesktopFindElementTool());
            Register(new DesktopClickTool());
            Register(new DesktopTypeTool());
            Register(new DesktopGetTextTool());
            Register(new DesktopKeyPressTool());
            Register(new DesktopScreenshotTool());
            Register(new DesktopCloseTool());
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
