using System.IO;
using System.Windows.Controls;

namespace OpenClaudeCodeWPF.Views
{
    public partial class FilePreviewPanel : UserControl
    {
        public FilePreviewPanel()
        {
            InitializeComponent();
        }

        public void ShowFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                FilePathText.Text = $"找不到檔案: {filePath}";
                FileContent.Text = "";
                return;
            }

            try
            {
                FilePathText.Text = filePath;
                FileContent.Text = File.ReadAllText(filePath);
            }
            catch (System.Exception ex)
            {
                FileContent.Text = $"無法讀取檔案: {ex.Message}";
            }
        }

        public void ShowContent(string title, string content)
        {
            FilePathText.Text = title;
            FileContent.Text = content;
        }

        public void Clear()
        {
            FilePathText.Text = "檔案預覽";
            FileContent.Text = "";
        }
    }
}
