using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

#nullable enable
namespace ChatAyi.Services.Documents;

/// <summary>
/// Reads text content from supported document formats: .txt, .md, .docx, text-based .pdf.
/// No OCR, no scanned documents. Max file size 15 MB, max extracted chars 80,000.
/// </summary>
public class DocumentReaderService
{
    private const long MaxFileSize = 15 * 1024 * 1024; // 15 MB
    private const int MaxExtractedChars = 80_000;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".docx", ".pdf"
    };

    public bool IsExtensionAllowed(string extension)
        => AllowedExtensions.Contains(extension);

    /// <summary>
    /// Read a document file and return extracted text. Returns null if file
    /// is missing, too large, or an unsupported format.
    /// </summary>
    public async Task<DocumentReadResult?> ReadAsync(string filePath)
    {
        var fi = new FileInfo(filePath);
        if (!fi.Exists) return null;
        if (fi.Length > MaxFileSize) return null;

        var ext = fi.Extension.ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return null;

        string fullText = ext switch
        {
            ".txt" or ".md" => await File.ReadAllTextAsync(filePath),
            ".docx" => ReadDocx(filePath),
            ".pdf" => ReadPdf(filePath),
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(fullText)) return null;

        fullText = Truncate(fullText);
        var format = ext switch
        {
            ".txt" => "txt",
            ".md" => "md",
            ".docx" => "docx",
            ".pdf" => "pdf",
            _ => "unknown"
        };

        return new DocumentReadResult(fi.Name, fullText, fullText.Length, format);
    }

    private static string Truncate(string text) =>
        text.Length <= MaxExtractedChars ? text : text[..MaxExtractedChars];

    private static string ReadDocx(string path)
    {
        using var doc = WordprocessingDocument.Open(path, false);
        var mainPart = doc.MainDocumentPart;
        if (mainPart?.Document?.Body is not Body body) return string.Empty;

        var paragraphs = body.Elements<Paragraph>()
            .Select(p => p.InnerText);
        return string.Join(Environment.NewLine, paragraphs);
    }

    private static string ReadPdf(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var pages = new List<string>();
        foreach (var page in pdf.GetPages())
        {
            pages.Add(page.Text);
        }
        return string.Join(Environment.NewLine + Environment.NewLine, pages);
    }
}
