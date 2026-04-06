namespace OpenClaudeCodeWPF.Models
{
    public class ToolResult
    {
        public bool IsSuccess { get; set; }
        public string Content { get; set; }
        public byte[] BinaryContent { get; set; }
        public string MimeType { get; set; }
        public string Error { get; set; }

        public static ToolResult Success(string content)
        {
            return new ToolResult { IsSuccess = true, Content = content };
        }

        public static ToolResult Failure(string error)
        {
            return new ToolResult { IsSuccess = false, Error = error, Content = $"Error: {error}" };
        }
    }
}
