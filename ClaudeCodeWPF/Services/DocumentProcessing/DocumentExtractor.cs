using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpenClaudeCodeWPF.Models;

namespace OpenClaudeCodeWPF.Services.DocumentProcessing
{
    public static class DocumentExtractor
    {
        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".csv", ".log", ".json", ".xml"
        };

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Extract text content from a document file.</summary>
        public static string ExtractText(string filePath)
        {
            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (TextExtensions.Contains(ext))
                    return File.ReadAllText(filePath, Encoding.UTF8);

                if (ext == ".pdf")
                    return ExtractPdfText(filePath);

                if (ext == ".docx")
                    return ExtractDocxText(filePath);

                if (ext == ".xlsx")
                    return ExtractXlsxText(filePath);

                if (ext == ".pptx")
                    return ExtractPptxText(filePath);

                if (ext == ".ppt")
                    return "Error: Legacy PPT format is not supported. Please convert to PPTX format.";

                return $"Error: Unsupported file type '{ext}'.";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Render document pages as images using external tools.
        /// Returns null if the tool is not available or the format is not supported.
        /// </summary>
        public static List<ImageBlock> ExtractImages(string filePath)
        {
            try
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                if (ext == ".pdf")
                    return ExtractPdfImages(filePath);

                if (ext == ".pptx")
                    return ExtractPptxImages(filePath);

                return null;
            }
            catch
            {
                return null;
            }
        }

        // ── Text Extractors ───────────────────────────────────────────────

        /// <summary>
        /// Pure-C# PDF text extraction with ToUnicode CMap support.
        /// Pass 1: decompress all streams, build glyph→Unicode map from beginbfchar/beginbfrange.
        /// Pass 2: scan content streams, decode both parenthesis (ASCII) and hex (CID) text strings.
        /// Handles modern Chinese/Japanese/Korean PDFs that use CID composite fonts.
        /// </summary>
        private static string ExtractPdfText(string filePath)
        {
            try
            {
                var fileBytes = File.ReadAllBytes(filePath);
                var latin1    = Encoding.GetEncoding("iso-8859-1");

                // ── Pass 1: collect all CMap glyph→Unicode mappings ──────────
                var cmap = BuildGlobalCMap(fileBytes, latin1);

                // ── Pass 2: extract text from content streams ─────────────────
                var sb        = new StringBuilder();
                byte[] streamTag    = Encoding.ASCII.GetBytes("stream");
                byte[] endstreamTag = Encoding.ASCII.GetBytes("endstream");

                int pos       = 0;
                int pageCount = 0;

                while (pos < fileBytes.Length)
                {
                    int streamStart = IndexOf(fileBytes, streamTag, pos);
                    if (streamStart < 0) break;

                    int contentStart = streamStart + streamTag.Length;
                    if (contentStart < fileBytes.Length && fileBytes[contentStart] == '\r') contentStart++;
                    if (contentStart < fileBytes.Length && fileBytes[contentStart] == '\n') contentStart++;

                    int streamEnd = IndexOf(fileBytes, endstreamTag, contentStart);
                    if (streamEnd < 0) break;

                    int    dataLen  = streamEnd - contentStart;
                    string dictText = Encoding.ASCII.GetString(fileBytes, Math.Max(0, streamStart - 512),
                                          Math.Min(512, streamStart));
                    bool isFlate = dictText.Contains("/FlateDecode") || dictText.Contains("/Fl ");

                    string content = DecompressStream(fileBytes, contentStart, dataLen, isFlate, latin1);

                    if (content != null && content.Contains("BT"))
                    {
                        pageCount++;
                        sb.AppendLine($"--- Page {pageCount} ---");
                        ExtractTextFromContentStream(content, sb, cmap);
                        sb.AppendLine();
                    }

                    pos = streamEnd + endstreamTag.Length;
                }

                if (sb.Length == 0)
                    return "(PDF text extraction: no readable text found.\n" +
                           "This PDF may be scanned/image-based or encrypted.\n" +
                           "Install Ghostscript to enable image-mode reading for multi-modal models.)";

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error reading PDF: {ex.Message}";
            }
        }

        /// <summary>
        /// Pass 1 of PDF extraction: scan ALL streams (decompressing FlateDecode ones),
        /// find beginbfchar / beginbfrange CMap sections and build a global glyph→char map.
        /// </summary>
        private static Dictionary<int, string> BuildGlobalCMap(byte[] fileBytes, Encoding latin1)
        {
            var map = new Dictionary<int, string>();

            byte[] streamTag    = Encoding.ASCII.GetBytes("stream");
            byte[] endstreamTag = Encoding.ASCII.GetBytes("endstream");

            int pos = 0;
            while (pos < fileBytes.Length)
            {
                int streamStart = IndexOf(fileBytes, streamTag, pos);
                if (streamStart < 0) break;

                int contentStart = streamStart + streamTag.Length;
                if (contentStart < fileBytes.Length && fileBytes[contentStart] == '\r') contentStart++;
                if (contentStart < fileBytes.Length && fileBytes[contentStart] == '\n') contentStart++;

                int streamEnd = IndexOf(fileBytes, endstreamTag, contentStart);
                if (streamEnd < 0) break;

                int    dataLen  = streamEnd - contentStart;
                string dictText = Encoding.ASCII.GetString(fileBytes, Math.Max(0, streamStart - 512),
                                      Math.Min(512, streamStart));
                bool isFlate = dictText.Contains("/FlateDecode") || dictText.Contains("/Fl ");

                string content = DecompressStream(fileBytes, contentStart, dataLen, isFlate, latin1);
                if (content != null && content.Contains("beginbfchar"))
                    ParseCMapIntoDict(content, map);

                pos = streamEnd + endstreamTag.Length;
            }

            return map;
        }

        /// <summary>Parse beginbfchar / beginbfrange sections into a glyph→Unicode-string map.</summary>
        private static void ParseCMapIntoDict(string cmapContent, Dictionary<int, string> map)
        {
            // beginbfchar: <glyphHex> <unicodeHex>
            int pos = 0;
            while ((pos = cmapContent.IndexOf("beginbfchar", pos, StringComparison.Ordinal)) >= 0)
            {
                int end = cmapContent.IndexOf("endbfchar", pos + 11, StringComparison.Ordinal);
                if (end < 0) break;
                string block = cmapContent.Substring(pos + 11, end - pos - 11);

                foreach (Match m in Regex.Matches(block, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                {
                    int glyph;
                    if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out glyph))
                    {
                        string uniHex = m.Groups[2].Value;
                        string uniStr = HexToUnicodeString(uniHex);
                        if (uniStr != null) map[glyph] = uniStr;
                    }
                }
                pos = end + 9;
            }

            // beginbfrange: <startGlyph> <endGlyph> <startUnicode>
            pos = 0;
            while ((pos = cmapContent.IndexOf("beginbfrange", pos, StringComparison.Ordinal)) >= 0)
            {
                int end = cmapContent.IndexOf("endbfrange", pos + 12, StringComparison.Ordinal);
                if (end < 0) break;
                string block = cmapContent.Substring(pos + 12, end - pos - 12);

                foreach (Match m in Regex.Matches(block,
                    @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
                {
                    int startGlyph, endGlyph, startUni;
                    if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out startGlyph) &&
                        int.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out endGlyph)   &&
                        int.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.HexNumber, null, out startUni))
                    {
                        for (int g = startGlyph; g <= endGlyph && g - startGlyph < 1024; g++)
                        {
                            int u = startUni + (g - startGlyph);
                            try { map[g] = char.ConvertFromUtf32(u); } catch { }
                        }
                    }
                }
                pos = end + 10;
            }
        }

        /// <summary>Convert a hex Unicode string (e.g., "8077" or "D87EDE44") to a .NET string.</summary>
        private static string HexToUnicodeString(string hex)
        {
            try
            {
                // Pairs of hex bytes → Unicode scalar
                if (hex.Length == 4)
                {
                    int cp;
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out cp))
                        return char.ConvertFromUtf32(cp);
                }
                else if (hex.Length == 8)
                {
                    // Surrogate pair encoded as UTF-16BE
                    int hi, lo;
                    if (int.TryParse(hex.Substring(0, 4), System.Globalization.NumberStyles.HexNumber, null, out hi) &&
                        int.TryParse(hex.Substring(4, 4), System.Globalization.NumberStyles.HexNumber, null, out lo))
                    {
                        byte[] utf16 = { (byte)(hi >> 8), (byte)(hi & 0xFF), (byte)(lo >> 8), (byte)(lo & 0xFF) };
                        return Encoding.BigEndianUnicode.GetString(utf16);
                    }
                }
                else
                {
                    // Variable-length: interpret as big-endian sequence of code units
                    if (hex.Length % 4 == 0)
                    {
                        var bytes = new byte[hex.Length / 2];
                        for (int i = 0; i < bytes.Length; i++)
                            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                        return Encoding.BigEndianUnicode.GetString(bytes);
                    }
                }
            }
            catch { }
            return null;
        }

        private static string DecompressStream(byte[] fileBytes, int contentStart, int dataLen,
                                               bool isFlate, Encoding latin1)
        {
            if (isFlate && dataLen > 2)
            {
                int deflateOffset = (fileBytes[contentStart] == 0x78) ? 2 : 0;
                try
                {
                    using (var ms      = new MemoryStream(fileBytes, contentStart + deflateOffset, dataLen - deflateOffset))
                    using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var reader  = new StreamReader(deflate, latin1))
                        return reader.ReadToEnd();
                }
                catch { return null; }
            }
            return latin1.GetString(fileBytes, contentStart, dataLen);
        }

        /// <summary>Scan BT...ET blocks in a content stream and extract text using the CMap.</summary>
        private static void ExtractTextFromContentStream(string content, StringBuilder output,
                                                         Dictionary<int, string> cmap)
        {
            int i = 0;
            while (i < content.Length)
            {
                int btPos = content.IndexOf("BT", i, StringComparison.Ordinal);
                if (btPos < 0) break;

                int etPos = content.IndexOf("ET", btPos + 2, StringComparison.Ordinal);
                if (etPos < 0) break;

                string block = content.Substring(btPos + 2, etPos - btPos - 2);
                ExtractTextFromBlock(block, output, cmap);

                i = etPos + 2;
            }
        }

        /// <summary>
        /// Extract text from a single BT...ET block.
        /// Handles:
        ///   (string) Tj / ' / "   — parenthesis ASCII/Latin-1 strings
        ///   [(string|&lt;hex&gt; num)...] TJ  — mixed array operator
        ///   &lt;hex&gt; Tj        — hex-encoded CID string (Chinese/Japanese/Korean)
        ///   Td / TD / T* / Tm    — newline operators
        /// </summary>
        private static void ExtractTextFromBlock(string block, StringBuilder sb,
                                                 Dictionary<int, string> cmap)
        {
            bool needsNewline = false;
            int i = 0;
            while (i < block.Length)
            {
                char c = block[i];

                // ── PDF newline operators: TD, Td, T*, Tm ────────────────
                if (c == 'T' && i + 1 < block.Length)
                {
                    char next = block[i + 1];
                    if (next == 'D' || next == 'd' || next == '*')
                    {
                        needsNewline = true;
                        i += 2;
                        continue;
                    }
                    if (next == 'm')
                    {
                        needsNewline = true;
                        i += 2;
                        continue;
                    }
                }

                // ── TJ array: [ ... ] TJ ────────────────────────────────
                if (c == '[')
                {
                    if (needsNewline && sb.Length > 0) { sb.AppendLine(); needsNewline = false; }
                    int j = i + 1;
                    while (j < block.Length && block[j] != ']')
                    {
                        if (block[j] == '(')
                        {
                            var str = ReadParenString(block, j, out j);
                            sb.Append(str);
                        }
                        else if (block[j] == '<')
                        {
                            var str = ReadHexString(block, j, cmap, out j);
                            sb.Append(str);
                        }
                        else j++;
                    }
                    i = j + 1;
                    continue;
                }

                // ── Single hex string: <hex> Tj ──────────────────────────
                if (c == '<' && i + 1 < block.Length && block[i + 1] != '<')
                {
                    int endHex = block.IndexOf('>', i + 1);
                    if (endHex > i)
                    {
                        int k = endHex + 1;
                        while (k < block.Length && (block[k] == ' ' || block[k] == '\n' || block[k] == '\r' || block[k] == '\t')) k++;
                        if (k + 1 < block.Length && block[k] == 'T' && block[k + 1] == 'j')
                        {
                            if (needsNewline && sb.Length > 0) { sb.AppendLine(); needsNewline = false; }
                            var str = ReadHexString(block, i, cmap, out _);
                            sb.Append(str);
                            i = k + 2;
                            continue;
                        }
                    }
                }

                // ── Parenthesis string: (text) Tj / ' ────────────────────
                if (c == '(')
                {
                    var str = ReadParenString(block, i, out int afterParen);
                    int k = afterParen;
                    while (k < block.Length && (block[k] == ' ' || block[k] == '\n' || block[k] == '\r' || block[k] == '\t')) k++;
                    if (k < block.Length)
                    {
                        char op1 = block[k];
                        char op2 = k + 1 < block.Length ? block[k + 1] : '\0';
                        if (needsNewline && sb.Length > 0) { sb.AppendLine(); needsNewline = false; }
                        if      (op1 == 'T' && op2 == 'j') sb.Append(str);
                        else if (op1 == '\'')               { sb.AppendLine(); sb.Append(str); }
                        else if (op1 == '"')                { sb.AppendLine(); sb.Append(str); }
                        else                                sb.Append(str);
                    }
                    else sb.Append(str);
                    i = afterParen;
                    continue;
                }

                i++;
            }
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.AppendLine();
        }

        private static string ReadParenString(string block, int start, out int afterClose)
        {
            var sb = new StringBuilder();
            int j = start + 1;
            while (j < block.Length && block[j] != ')')
            {
                if (block[j] == '\\' && j + 1 < block.Length)
                {
                    j++;
                    switch (block[j])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default:  sb.Append(block[j]); break;
                    }
                }
                else sb.Append(block[j]);
                j++;
            }
            afterClose = j + 1;
            return sb.ToString();
        }

        /// <summary>
        /// Decode a &lt;hex&gt; string using the CMap. Each 2-byte (4 hex digit) group = one glyph.
        /// Falls back to direct Unicode code-point if glyph not in CMap.
        /// </summary>
        private static string ReadHexString(string block, int start, Dictionary<int, string> cmap, out int afterClose)
        {
            int end = block.IndexOf('>', start + 1);
            if (end < 0) { afterClose = start + 1; return ""; }

            string hex = block.Substring(start + 1, end - start - 1).Trim();
            afterClose = end + 1;

            if (string.IsNullOrEmpty(hex)) return "";

            // Pad to even length
            if (hex.Length % 2 != 0) hex = "0" + hex;

            var result = new StringBuilder();

            // Try 2-byte (4 hex chars) glyph groups first (CID fonts)
            if (cmap.Count > 0 && hex.Length % 4 == 0)
            {
                bool allMapped = true;
                var temp = new StringBuilder();
                for (int i = 0; i < hex.Length; i += 4)
                {
                    int glyph;
                    if (int.TryParse(hex.Substring(i, 4), System.Globalization.NumberStyles.HexNumber, null, out glyph))
                    {
                        string mapped;
                        if (cmap.TryGetValue(glyph, out mapped)) temp.Append(mapped);
                        else { allMapped = false; break; }
                    }
                }
                if (allMapped) return temp.ToString();
            }

            // Fall back: try 1-byte groups (standard encoding)
            for (int i = 0; i < hex.Length; i += 2)
            {
                int b;
                if (int.TryParse(hex.Substring(i, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
                {
                    string mapped;
                    if (cmap.TryGetValue(b, out mapped)) result.Append(mapped);
                    else if (b >= 0x20 && b < 0x7F)      result.Append((char)b);
                    // else: skip non-printable / unmapped
                }
            }
            return result.ToString();
        }

        // ZipArchive-based extractors (no System.IO.Compression.FileSystem needed)

        private static string ExtractDocxText(string filePath)
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var sb = new StringBuilder();

            using (var fs  = File.OpenRead(filePath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var entry = zip.GetEntry("word/document.xml");
                if (entry == null)
                    return "Error: word/document.xml not found in DOCX archive.";

                using (var stream = entry.Open())
                {
                    var doc = XDocument.Load(stream);

                    // Reconstruct paragraphs
                    foreach (var para in doc.Descendants(w + "p"))
                    {
                        foreach (var t in para.Descendants(w + "t"))
                            sb.Append(t.Value);
                        sb.AppendLine();
                    }
                }
            }
            return sb.ToString().Trim();
        }

        private static string ExtractXlsxText(string filePath)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sharedStrings = new List<string>();
            var sb = new StringBuilder();

            using (var fs  = File.OpenRead(filePath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                // Load shared strings table
                var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using (var stream = ssEntry.Open())
                    {
                        var doc = XDocument.Load(stream);
                        foreach (var si in doc.Descendants(ns + "si"))
                        {
                            var text = string.Concat(si.Descendants(ns + "t").Select(t => t.Value));
                            sharedStrings.Add(text);
                        }
                    }
                }

                // Load each worksheet
                var sheetEntries = zip.Entries
                    .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                             && e.FullName.EndsWith(".xml",                  StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName)
                    .ToList();

                int sheetIndex = 0;
                foreach (var sheetEntry in sheetEntries)
                {
                    sheetIndex++;
                    sb.AppendLine($"=== Sheet {sheetIndex} ===");

                    using (var stream = sheetEntry.Open())
                    {
                        var doc = XDocument.Load(stream);
                        foreach (var row in doc.Descendants(ns + "row"))
                        {
                            var cells = new List<string>();
                            foreach (var c in row.Descendants(ns + "c"))
                            {
                                var t = c.Attribute("t")?.Value;
                                var v = c.Element(ns + "v")?.Value ?? "";
                                string cellValue;
                                int idx;
                                if (t == "s" && int.TryParse(v, out idx) && idx < sharedStrings.Count)
                                    cellValue = sharedStrings[idx];
                                else if (t == "inlineStr")
                                    cellValue = string.Concat(c.Descendants(ns + "t").Select(x => x.Value));
                                else
                                    cellValue = v;
                                cells.Add(cellValue);
                            }
                            sb.AppendLine(string.Join("\t", cells));
                        }
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString().Trim();
        }

        private static string ExtractPptxText(string filePath)
        {
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var sb = new StringBuilder();

            using (var fs  = File.OpenRead(filePath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var slideEntries = zip.Entries
                    .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                             && e.FullName.EndsWith(".xml",               StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName)
                    .ToList();

                int slideNum = 0;
                foreach (var slideEntry in slideEntries)
                {
                    slideNum++;
                    sb.AppendLine($"--- Slide {slideNum} ---");

                    using (var stream = slideEntry.Open())
                    {
                        var doc = XDocument.Load(stream);
                        foreach (var t in doc.Descendants(a + "t"))
                        {
                            sb.Append(t.Value);
                            sb.Append(' ');
                        }
                    }
                    sb.AppendLine();
                    sb.AppendLine();
                }
            }
            return sb.ToString().Trim();
        }

        // ── Image Extractors (external tools: Ghostscript / LibreOffice) ──

        private static List<ImageBlock> ExtractPdfImages(string filePath)
        {
            var gsPath = FindGhostscript();
            if (gsPath == null) return null;

            var tmpDir = CreateTempDirectory();
            try
            {
                var outputPattern = Path.Combine(tmpDir, "page%d.png");
                var args = $"-dBATCH -dNOPAUSE -sDEVICE=png16m -r96 -sOutputFile=\"{outputPattern}\" \"{filePath}\"";
                if (RunProcess(gsPath, args, 120) != 0) return null;

                return ReadPngFiles(tmpDir, "page*.png", "Page");
            }
            finally { DeleteDirectory(tmpDir); }
        }

        private static List<ImageBlock> ExtractPptxImages(string filePath)
        {
            var sofficePath = FindExecutable("soffice.exe", new[]
            {
                @"C:\Program Files\LibreOffice\program",
                @"C:\Program Files (x86)\LibreOffice\program"
            });
            if (sofficePath == null) return null;

            var tmpDir = CreateTempDirectory();
            try
            {
                var args = $"--headless --convert-to png --outdir \"{tmpDir}\" \"{filePath}\"";
                if (RunProcess(sofficePath, args, 120) != 0) return null;

                return ReadPngFiles(tmpDir, "*.png", "Slide");
            }
            finally { DeleteDirectory(tmpDir); }
        }

        /// <summary>Locate Ghostscript, including versioned subdirectories.</summary>
        private static string FindGhostscript()
        {
            var path = FindExecutable("gs.exe", new[]
            {
                @"C:\Program Files\gs",
                @"C:\Program Files (x86)\gs"
            });
            if (path != null) return path;

            foreach (var baseDir in new[] { @"C:\Program Files\gs", @"C:\Program Files (x86)\gs" })
            {
                if (!Directory.Exists(baseDir)) continue;
                foreach (var subDir in Directory.GetDirectories(baseDir))
                {
                    var candidate = Path.Combine(subDir, "bin", "gs.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static List<ImageBlock> ReadPngFiles(string directory, string searchPattern, string captionPrefix)
        {
            var images = new List<ImageBlock>();
            var files  = Directory.GetFiles(directory, searchPattern)
                                  .OrderBy(f => f)
                                  .Take(20)
                                  .ToList();

            for (int i = 0; i < files.Count; i++)
            {
                images.Add(new ImageBlock
                {
                    Base64Data = Convert.ToBase64String(File.ReadAllBytes(files[i])),
                    MimeType   = "image/png",
                    Caption    = $"{captionPrefix} {i + 1} of {files.Count}"
                });
            }
            return images;
        }

        private static string FindExecutable(string exeName, string[] extraPaths)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            foreach (var dir in extraPaths)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                var candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        private static int RunProcess(string executable, string arguments, int timeoutSeconds)
        {
            var psi = new ProcessStartInfo
            {
                FileName               = executable,
                Arguments              = arguments,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardError  = true
            };

            using (var proc = Process.Start(psi))
            {
                if (proc == null) return -1;
                proc.WaitForExit(timeoutSeconds * 1000);
                if (!proc.HasExited) { proc.Kill(); return -1; }
                return proc.ExitCode;
            }
        }

        private static string CreateTempDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "DocExtract_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void DeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { /* best-effort cleanup */ }
        }

        /// <summary>Boyer-Moore-Horspool style byte array search (simple version).</summary>
        private static int IndexOf(byte[] haystack, byte[] needle, int startPos)
        {
            for (int i = startPos; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
