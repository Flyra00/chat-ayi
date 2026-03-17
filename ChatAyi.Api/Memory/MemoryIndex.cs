using System.Text;

namespace ChatAyi.Api.Memory;

public sealed class MemoryIndex
{
    private sealed record Chunk(string Source, string Text, string Haystack);

    public sealed record MemorySnippet(string Source, string Text, int Score);
    public sealed record MemorySearchResult(string Context, List<MemorySnippet> Snippets);

    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<MemoryIndex> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<Chunk> _chunks = new();
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;

    public MemoryIndex(IHostEnvironment env, IConfiguration config, ILogger<MemoryIndex> logger)
    {
        _env = env;
        _config = config;
        _logger = logger;
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _chunks = await LoadChunksAsync(ct);
            _loadedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation("Loaded {ChunkCount} memory chunks", _chunks.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetContextAsync(string query, CancellationToken ct)
    {
        var result = await SearchAsync(query, ct);
        return result.Context;
    }

    public async Task<MemorySearchResult> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new MemorySearchResult(string.Empty, new List<MemorySnippet>());

        // Lazy load on first use.
        if (_loadedAt == DateTimeOffset.MinValue)
        {
            await ReloadAsync(ct);
        }

        var maxSnippets = Math.Max(1, _config.GetValue<int?>("Memory:MaxSnippets") ?? 4);
        var maxChars = Math.Max(500, _config.GetValue<int?>("Memory:MaxChars") ?? 3500);
        var tokens = UniqueTokens(query);
        if (tokens.Count == 0)
            return new MemorySearchResult(string.Empty, new List<MemorySnippet>());
        if (_chunks.Count == 0)
            return new MemorySearchResult(string.Empty, new List<MemorySnippet>());

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

    public async Task<string> AppendLongTermAsync(string text, CancellationToken ct)
    {
        var root = GetWorkspaceRoot();
        var fileName = _config["Memory:LongTermFile"] ?? "MEMORY.md";
        var path = Path.Combine(root, fileName);

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);

        var clean = (text ?? string.Empty).Trim();
        if (clean.Length == 0) return path;

        var block = $"\n- {clean.Replace("\r\n", "\n").Replace("\r", "\n").Trim()}\n";
        await File.AppendAllTextAsync(path, block, ct);

        await ReloadAsync(ct);
        return path;
    }

    public async Task<string> AppendLongTermManyAsync(IEnumerable<string> items, CancellationToken ct)
    {
        var root = GetWorkspaceRoot();
        var fileName = _config["Memory:LongTermFile"] ?? "MEMORY.md";
        var path = Path.Combine(root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);

        var list = (items ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Take(50)
            .ToList();

        if (list.Count == 0) return path;

        var sb = new StringBuilder();
        sb.Append('\n');
        foreach (var item in list)
        {
            sb.Append("- ");
            sb.AppendLine(item.Replace("\r\n", "\n").Replace("\r", "\n").Trim());
        }

        await File.AppendAllTextAsync(path, sb.ToString(), ct);
        await ReloadAsync(ct);
        return path;
    }

    public async Task<string> AppendDailyAsync(string text, DateOnly? date, CancellationToken ct)
    {
        var root = GetWorkspaceRoot();
        var dailyDir = _config["Memory:DailyDir"] ?? "memory";
        var folder = Path.Combine(root, dailyDir);
        Directory.CreateDirectory(folder);

        var d = date ?? DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(folder, $"{d:yyyy-MM-dd}.md");

        var clean = (text ?? string.Empty).Trim();
        if (clean.Length == 0) return path;

        var now = DateTimeOffset.Now;
        var block = $"\n## {now:HH:mm}\n{clean.Replace("\r\n", "\n").Replace("\r", "\n").Trim()}\n";
        await File.AppendAllTextAsync(path, block, ct);

        await ReloadAsync(ct);
        return path;
    }

    public async Task<string> AppendDailyManyAsync(IEnumerable<string> items, DateOnly? date, CancellationToken ct)
    {
        var root = GetWorkspaceRoot();
        var dailyDir = _config["Memory:DailyDir"] ?? "memory";
        var folder = Path.Combine(root, dailyDir);
        Directory.CreateDirectory(folder);

        var d = date ?? DateOnly.FromDateTime(DateTime.Now);
        var path = Path.Combine(folder, $"{d:yyyy-MM-dd}.md");

        var list = (items ?? Array.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Take(50)
            .ToList();

        if (list.Count == 0) return path;

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
        return path;
    }

    private async Task<List<Chunk>> LoadChunksAsync(CancellationToken ct)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".md", ".txt", ".json" };
        var files = new List<string>();

        // OpenClaw-like workspace layout
        // - workspace/MEMORY.md
        // - workspace/memory/YYYY-MM-DD.md
        var workspaceRoot = GetWorkspaceRoot();
        var longTermFile = _config["Memory:LongTermFile"] ?? "MEMORY.md";
        var longTermPath = Path.Combine(workspaceRoot, longTermFile);
        if (File.Exists(longTermPath) && allowed.Contains(Path.GetExtension(longTermPath)))
            files.Add(longTermPath);

        var dailyDir = _config["Memory:DailyDir"] ?? "memory";
        var dailyPath = Path.Combine(workspaceRoot, dailyDir);
        if (Directory.Exists(dailyPath))
        {
            files.AddRange(Directory.EnumerateFiles(dailyPath, "*.*", SearchOption.AllDirectories)
                .Where(f => allowed.Contains(Path.GetExtension(f))));
        }

        // Backward-compatible directory (older config)
        var legacyDir = _config["Memory:Directory"] ?? "memory";
        var legacyPath = Path.IsPathRooted(legacyDir) ? legacyDir : Path.Combine(_env.ContentRootPath, legacyDir);
        if (!string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(dailyPath), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(legacyPath))
        {
            files.AddRange(Directory.EnumerateFiles(legacyPath, "*.*", SearchOption.AllDirectories)
                .Where(f => allowed.Contains(Path.GetExtension(f))));
        }

        if (files.Count == 0)
        {
            _logger.LogWarning("No memory files found under workspace: {WorkspaceRoot}", workspaceRoot);
            return new List<Chunk>();
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

            var rel = Path.GetRelativePath(_env.ContentRootPath, file).Replace('\\', '/');
            foreach (var c in ChunkText(raw))
            {
                chunks.Add(new Chunk(rel, c, Normalize(c)));
            }
        }

        return chunks;
    }

    private string GetWorkspaceRoot()
    {
        var root = _config["Workspace:Root"] ?? "workspace";
        return Path.IsPathRooted(root) ? root : Path.Combine(_env.ContentRootPath, root);
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
