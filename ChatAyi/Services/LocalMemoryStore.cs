using System.Text;

namespace ChatAyi.Services;

public sealed class LocalMemoryStore
{
    private sealed record Chunk(string Source, string Text, string Haystack);

    public sealed record MemorySnippet(string Source, string Text, int Score);
    public sealed record MemorySearchResult(string Context, List<MemorySnippet> Snippets);

    private List<Chunk> _chunks = new();
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string GetWorkspaceRoot()
        => Path.Combine(FileSystem.AppDataDirectory, "workspace");

    public string GetLongTermPath()
        => Path.Combine(GetWorkspaceRoot(), "MEMORY.md");

    public string GetDailyDir()
        => Path.Combine(GetWorkspaceRoot(), "memory");

    public async Task EnsureInitializedAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(GetWorkspaceRoot());
        Directory.CreateDirectory(GetDailyDir());

        var mem = GetLongTermPath();
        if (!File.Exists(mem))
        {
            var seed = "# Long-term Memory\n\n- Language: Indonesian\n";
            await File.WriteAllTextAsync(mem, seed, ct);
        }
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        await _gate.WaitAsync(ct);
        try
        {
            _chunks = await LoadChunksAsync(ct);
            _loadedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MemorySearchResult> SearchAsync(string query, int maxSnippets, int maxChars, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new MemorySearchResult(string.Empty, new List<MemorySnippet>());

        if (_loadedAt == DateTimeOffset.MinValue)
            await ReloadAsync(ct);

        var tokens = UniqueTokens(query);
        if (tokens.Count == 0 || _chunks.Count == 0)
            return new MemorySearchResult(string.Empty, new List<MemorySnippet>());

        maxSnippets = Math.Clamp(maxSnippets, 1, 12);
        maxChars = Math.Max(500, maxChars);

        var scored = _chunks
            .Select(c => (chunk: c, score: Score(c.Haystack, tokens)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ToList();

        if (scored.Count == 0)
            return new MemorySearchResult(string.Empty, new List<MemorySnippet>());

        var sb = new StringBuilder();
        var snippets = new List<MemorySnippet>();
        foreach (var (chunk, score) in scored.Take(maxSnippets))
        {
            var block = $"[{chunk.Source}]\n{chunk.Text.Trim()}\n\n";
            if (sb.Length + block.Length > maxChars) break;
            sb.Append(block);
            snippets.Add(new MemorySnippet(chunk.Source, chunk.Text.Trim(), score));
        }

        return new MemorySearchResult(sb.ToString().Trim(), snippets);
    }

    public async Task<string> GetContextAsync(string query, CancellationToken ct)
        => (await SearchAsync(query, maxSnippets: 4, maxChars: 3500, ct)).Context;

    public async Task AppendLongTermManyAsync(IEnumerable<string> items, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var list = (items ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Take(50)
            .ToList();
        if (list.Count == 0) return;

        var sb = new StringBuilder();
        sb.Append('\n');
        foreach (var item in list)
        {
            sb.Append("- ");
            sb.AppendLine(item.Replace("\r\n", "\n").Replace("\r", "\n").Trim());
        }

        await File.AppendAllTextAsync(GetLongTermPath(), sb.ToString(), ct);
        await ReloadAsync(ct);
    }

    public async Task AppendDailyManyAsync(IEnumerable<string> items, DateOnly? date, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);

        var list = (items ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Take(50)
            .ToList();
        if (list.Count == 0) return;

        var d = date ?? DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(GetDailyDir(), $"{d:yyyy-MM-dd}.md");

        var now = DateTimeOffset.Now;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"## {now:HH:mm}");
        foreach (var item in list)
        {
            sb.Append("- ");
            sb.AppendLine(item.Replace("\r\n", "\n").Replace("\r", "\n").Trim());
        }

        await File.AppendAllTextAsync(path, sb.ToString(), ct);
        await ReloadAsync(ct);
    }

    private async Task<List<Chunk>> LoadChunksAsync(CancellationToken ct)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".txt", ".json" };
        var files = new List<string>();

        var longTermPath = GetLongTermPath();
        if (File.Exists(longTermPath) && allowed.Contains(Path.GetExtension(longTermPath)))
            files.Add(longTermPath);

        var dailyDir = GetDailyDir();
        if (Directory.Exists(dailyDir))
        {
            files.AddRange(Directory.EnumerateFiles(dailyDir, "*.*", SearchOption.AllDirectories)
                .Where(f => allowed.Contains(Path.GetExtension(f))));
        }

        var chunks = new List<Chunk>();
        foreach (var file in files)
        {
            string raw;
            try
            {
                raw = await File.ReadAllTextAsync(file, ct);
            }
            catch
            {
                continue;
            }

            var rel = Path.GetFileName(file);
            if (file.EndsWith("MEMORY.md", StringComparison.OrdinalIgnoreCase))
                rel = "workspace/MEMORY.md";
            else
                rel = "workspace/memory/" + Path.GetFileName(file);

            foreach (var c in ChunkText(raw))
            {
                chunks.Add(new Chunk(rel, c, Normalize(c)));
            }
        }

        return chunks;
    }

    private static List<string> ChunkText(string text, int chunkSize = 1200, int overlap = 200)
    {
        var cleaned = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) return new List<string>();

        var parts = cleaned.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();

        var current = string.Empty;
        foreach (var partRaw in parts)
        {
            var part = partRaw.Trim();
            var candidate = string.IsNullOrEmpty(current) ? part : current + "\n\n" + part;
            if (candidate.Length <= chunkSize)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrEmpty(current)) chunks.Add(current);

            if (part.Length > chunkSize)
            {
                var i = 0;
                while (i < part.Length)
                {
                    var end = Math.Min(i + chunkSize, part.Length);
                    chunks.Add(part.Substring(i, end - i));
                    if (end == part.Length) break;
                    i = Math.Max(0, end - overlap);
                }
                current = string.Empty;
            }
            else
            {
                current = part;
            }
        }

        if (!string.IsNullOrEmpty(current)) chunks.Add(current);
        return chunks;
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in (text ?? string.Empty).ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == ' ') sb.Append(ch);
            else if (char.IsWhiteSpace(ch)) sb.Append(' ');
            else sb.Append(' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<string> UniqueTokens(string query)
    {
        var normalized = Normalize(query);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return tokens;
    }

    private static int Score(string haystack, List<string> tokens)
    {
        var score = 0;
        foreach (var t in tokens)
        {
            score += CountOccurrences(haystack, t);
        }
        return score;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle) || string.IsNullOrEmpty(haystack)) return 0;

        var count = 0;
        var idx = 0;
        while (true)
        {
            idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal);
            if (idx < 0) break;
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
