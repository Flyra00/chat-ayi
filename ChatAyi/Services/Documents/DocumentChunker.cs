namespace ChatAyi.Services.Documents;

/// <summary>
/// Splits document text into overlapping chunks and retrieves relevant
/// chunks for a user query using simple keyword overlap scoring.
/// V1 uses no embeddings, no vector DB — just stopword-filtered keyword matching.
/// </summary>
public class DocumentChunker
{
    private const int ChunkSize = 2000;
    private const int Overlap = 200;
    private const int MaxChunks = 8;

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // English
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "shall", "can", "need", "dare", "ought",
        "used", "to", "of", "in", "for", "on", "with", "at", "by", "from",
        "as", "into", "through", "during", "before", "after", "above", "below",
        "between", "out", "off", "over", "under", "again", "further", "then",
        "once", "here", "there", "when", "where", "why", "how", "all", "each",
        "every", "both", "few", "more", "most", "other", "some", "such", "no",
        "nor", "not", "only", "own", "same", "so", "than", "too", "very",
        "just", "because", "but", "and", "or", "if", "while", "that", "this",
        "these", "those", "it", "its",
        // Indonesian
        "dan", "di", "ke", "dari", "yang", "ini", "itu", "adalah", "untuk",
        "dengan", "pada", "tidak", "akan", "dalam", "saya", "kamu", "dia",
        "kami", "mereka", "sudah", "telah", "bisa", "ada", "juga", "saja",
        "sudah", "belum", "selalu", "sering", "karena", "jika", "maka",
        "tentang", "secara", "seperti", "setelah", "antara", "sebagai"
    };

    /// <summary>
    /// Splits text into overlapping chunks of roughly 2000 chars with 200 char overlap.
    /// Tries to break at paragraph boundaries first, then sentence boundaries.
    /// </summary>
    public List<DocumentChunk> ChunkText(string text, string sourceLabel = "doc")
    {
        var chunks = new List<DocumentChunk>();
        int start = 0;
        int index = 0;

        while (start < text.Length)
        {
            int end = Math.Min(start + ChunkSize, text.Length);

            // Try to break at a natural boundary if not at end
            if (end < text.Length)
            {
                int boundary = FindChunkBoundary(text, end);
                if (boundary > start)
                    end = boundary;
            }

            var chunkText = text[start..end].Trim();
            if (!string.IsNullOrEmpty(chunkText))
            {
                chunks.Add(new DocumentChunk(index, chunkText, $"{sourceLabel} (chunk {index + 1})"));
                index++;
            }

            start = end - Overlap;
            if (start <= 0 || start >= text.Length - 1)
                break;
        }

        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            // Edge case: text shorter than chunk size
            chunks.Add(new DocumentChunk(0, text.Trim(), $"{sourceLabel} (chunk 1)"));
        }

        return chunks;
    }

    /// <summary>
    /// Finds the best chunk boundary near a position, preferring paragraph breaks.
    /// </summary>
    private static int FindChunkBoundary(string text, int fromPos)
    {
        int searchStart = Math.Max(0, fromPos - ChunkSize / 2);
        int searchLen = fromPos - searchStart;

        // Look for double newline first (paragraph boundary)
        int doubleNewline = text.LastIndexOf("\n\n", fromPos - 1, searchLen);
        if (doubleNewline > searchStart)
            return doubleNewline + 2;

        // Look for single newline
        int newline = text.LastIndexOf('\n', fromPos - 1, searchLen);
        if (newline > searchStart)
            return newline + 1;

        // Look for sentence end
        int sentenceEnd = text.LastIndexOf(". ", fromPos - 1, searchLen);
        if (sentenceEnd > searchStart)
            return sentenceEnd + 2;

        return fromPos;
    }

    /// <summary>
    /// Scores and returns the most relevant chunks for a user query
    /// using simple keyword overlap.
    /// </summary>
    public List<DocumentChunk> RetrieveRelevantChunks(string query, List<DocumentChunk> chunks)
    {
        if (string.IsNullOrWhiteSpace(query) || chunks.Count == 0)
            return chunks.Take(MaxChunks).ToList();

        var queryWords = ExtractKeywords(query);
        if (queryWords.Count == 0)
            return chunks.Take(MaxChunks).ToList();

        var scored = chunks
            .Select(chunk => (
                Chunk: chunk,
                Score: ScoreChunk(queryWords, chunk.Text)
            ))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Chunk.Index)
            .Take(MaxChunks)
            .Select(x => x.Chunk)
            .ToList();

        // If at least one scored chunk has a keyword match, return scored order.
        // Otherwise fall back to first N chunks in order.
        bool anyMatch = scored.Any(c =>
            queryWords.Any(w => c.Text.Contains(w, StringComparison.OrdinalIgnoreCase)));

        return anyMatch ? scored : chunks.Take(MaxChunks).ToList();
    }

    /// <summary>
    /// Extract meaningful keywords from text, filtering stopwords.
    /// </summary>
    private static HashSet<string> ExtractKeywords(string text)
    {
        var separators = new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?',
            '"', '\'', '(', ')', '[', ']', '{', '}', '-', '_', '/', '\\', '|', '@', '#', '$', '%', '^', '&', '*', '+', '=', '<', '>', '~', '`' };

        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = word.Trim().ToLowerInvariant();
            if (clean.Length > 2 && !Stopwords.Contains(clean) && clean.All(char.IsLetterOrDigit))
                keywords.Add(clean);
        }
        return keywords;
    }

    /// <summary>
    /// Score a chunk by how many query keywords it contains.
    /// Score = matches² / queryWordCount (quadratic boost for multi-match chunks).
    /// </summary>
    private static double ScoreChunk(HashSet<string> queryKeywords, string chunkText)
    {
        var chunkWords = ExtractKeywords(chunkText);
        if (chunkWords.Count == 0) return 0;

        int matches = queryKeywords.Count(kw => chunkWords.Contains(kw));
        if (matches == 0) return 0;

        return (double)(matches * matches) / queryKeywords.Count;
    }
}
