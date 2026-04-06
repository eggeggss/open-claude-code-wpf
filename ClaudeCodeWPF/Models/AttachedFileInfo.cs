using System.IO;

namespace OpenClaudeCodeWPF.Models
{
    public class AttachedFileInfo
    {
        public string FilePath { get; set; }
        public string FileName => Path.GetFileName(FilePath);
        public long FileSize { get; set; }
        public string DisplaySize
        {
            get
            {
                if (FileSize >= 1024 * 1024)
                    return $"{(double)FileSize / 1024 / 1024:F1} MB";
                if (FileSize >= 1024)
                    return $"{FileSize / 1024} KB";
                return $"{FileSize} B";
            }
        }

        // Icon based on extension
        public string FileIcon
        {
            get
            {
                var ext = Path.GetExtension(FilePath)?.ToLowerInvariant();
                switch (ext)
                {
                    case ".pdf":  return "📄";
                    case ".doc":
                    case ".docx": return "📝";
                    case ".xls":
                    case ".xlsx": return "📊";
                    case ".ppt":
                    case ".pptx": return "📋";
                    case ".png":
                    case ".jpg":
                    case ".jpeg":
                    case ".gif":
                    case ".bmp":  return "🖼";
                    case ".txt":
                    case ".md":   return "📃";
                    default:      return "📎";
                }
            }
        }
    }
}
