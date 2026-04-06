namespace OpenClaudeCodeWPF.Models
{
    public class ImageBlock
    {
        public string Base64Data { get; set; }
        public string MimeType { get; set; }  // "image/png", "image/jpeg"
        public string Caption { get; set; }    // e.g. "Page 1 of 5"
    }
}
