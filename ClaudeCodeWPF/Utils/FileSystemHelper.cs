using System;
using System.IO;
using System.Text;

namespace OpenClaudeCodeWPF.Utils
{
    public static class FileSystemHelper
    {
        public static string ResolvePath(string path, string basePath = null)
        {
            if (string.IsNullOrEmpty(path)) return Environment.CurrentDirectory;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(basePath ?? Environment.CurrentDirectory, path);
            return Path.GetFullPath(path);
        }

        public static void EnsureDirectory(string dirPath)
        {
            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);
        }

        public static string SafeReadAllText(string path)
        {
            try { return File.ReadAllText(path, Encoding.UTF8); }
            catch { return null; }
        }

        public static bool SafeWriteAllText(string path, string content)
        {
            try
            {
                EnsureDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, content, Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }

        public static string GetMimeType(string filePath)
        {
            var ext = Path.GetExtension(filePath)?.ToLower();
            switch (ext)
            {
                case ".cs": case ".ts": case ".js": case ".py": case ".java": return "text/plain";
                case ".json": return "application/json";
                case ".html": return "text/html";
                case ".xml": return "text/xml";
                case ".md": return "text/markdown";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                default: return "application/octet-stream";
            }
        }
    }
}
