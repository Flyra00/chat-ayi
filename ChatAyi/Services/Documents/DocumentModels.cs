namespace ChatAyi.Services.Documents;

/// <summary>
/// A single chunk of document text with its position and source label.
/// </summary>
public record DocumentChunk(int Index, string Text, string SourceLabel);

/// <summary>
/// Result from reading a document file.
/// </summary>
public record DocumentReadResult(string FileName, string FullText, int TotalChars, string Format);

/// <summary>
/// Per-session document context that persists for the lifetime of a chat session.
/// </summary>
public class DocumentContext
{
    public string FileName { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public List<DocumentChunk> Chunks { get; set; } = new();
    public string Format { get; set; } = string.Empty;
    public int TotalChars { get; set; }
    public DateTime AttachedAt { get; set; } = DateTime.UtcNow;
}
