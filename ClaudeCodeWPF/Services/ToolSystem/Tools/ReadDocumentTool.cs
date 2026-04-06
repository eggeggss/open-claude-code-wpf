using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OpenClaudeCodeWPF.Models;
using OpenClaudeCodeWPF.Services;
using OpenClaudeCodeWPF.Services.DocumentProcessing;

namespace OpenClaudeCodeWPF.Services.ToolSystem.Tools
{
    public class ReadDocumentTool : IToolExecutor
    {
        public string Name => "ReadDocument";

        public string Description => "Read and extract content from documents: PDF, DOCX, XLSX, PPTX, TXT, MD, CSV and more. If the current model supports vision and the document is a PDF or PPTX, pages are rendered as images for multimodal analysis.";

        public JObject InputSchema => JObject.Parse(@"{
            ""type"": ""object"",
            ""properties"": {
                ""file_path"": { ""type"": ""string"", ""description"": ""Absolute or relative path to the document file"" },
                ""as_images"": { ""type"": ""boolean"", ""description"": ""Force image rendering even for text-capable models (optional)"" },
                ""max_pages"": { ""type"": ""integer"", ""description"": ""Maximum pages/slides to process (default: 20)"" }
            },
            ""required"": [""file_path""]
        }");

        public async Task<ToolResult> ExecuteAsync(JObject input, CancellationToken ct = default(CancellationToken))
        {
            var path = input["file_path"]?.ToString();
            if (string.IsNullOrEmpty(path))
                return ToolResult.Failure("file_path is required");

            // Resolve path
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Environment.CurrentDirectory, path);
            path = Path.GetFullPath(path);

            if (!File.Exists(path))
                return ToolResult.Failure($"File not found: {path}");

            var ext = Path.GetExtension(path).ToLowerInvariant();
            bool asImages = input["as_images"]?.Value<bool>() ?? false;
            bool visionModel = ConfigService.Instance.CurrentModelSupportsVision;
            bool shouldRenderImages = (asImages || visionModel) && (ext == ".pdf" || ext == ".pptx" || ext == ".ppt");

            if (shouldRenderImages)
            {
                var images = await Task.Run(() => DocumentExtractor.ExtractImages(path), ct);
                if (images != null && images.Count > 0)
                {
                    int maxPages = input["max_pages"]?.Value<int>() ?? 20;
                    if (images.Count > maxPages)
                        images = images.Take(maxPages).ToList();
                    var summary = $"[Document: {Path.GetFileName(path)}] Rendered {images.Count} page(s) as images for visual analysis.";
                    return ToolResult.SuccessWithImages(summary, images);
                }
                // Fallback to text if image rendering not available
            }

            // Text extraction path
            var text = await Task.Run(() => DocumentExtractor.ExtractText(path), ct);
            var header = $"[Document: {Path.GetFileName(path)} | Type: {ext.TrimStart('.')}]\n\n";
            return ToolResult.Success(header + text);
        }
    }
}
