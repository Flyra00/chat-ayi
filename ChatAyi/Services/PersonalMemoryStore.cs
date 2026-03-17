using System.Text;
using System.Text.Json;
using ChatAyi.Models;

namespace ChatAyi.Services;

public sealed class PersonalMemoryStore
{
    private const string MemoryFileName = "personal-memory.json";
    private const int MaxRelevantItems = 5;
    private const int MaxRelevantContentChars = 900;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string GetFilePath()
        => Path.Combine(FileSystem.AppDataDirectory, MemoryFileName);

    public async Task<IReadOnlyList<PersonalMemoryItem>> ListAsync(CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadDocumentInternalAsync(ct);
            return doc.Items
                .Where(x => !x.IsDeleted)
                .OrderByDescending(x => x.UpdatedUtc)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<PersonalMemoryItem> AddAsync(string category, string content, CancellationToken ct)
    {
        var trimmed = (content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Content is required.", nameof(content));

        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadDocumentInternalAsync(ct);
            var nowUtc = DateTimeOffset.UtcNow;
            var normalizedCategory = PersonalMemoryItem.NormalizeCategory(category);
            var hasExistingIdentity = doc.Items.Any(x => !x.IsDeleted && IsIdentityNameMemory(x));
            var identityName = TryExtractIdentityName(trimmed, hasExistingIdentity);
            var finalCategory = string.IsNullOrWhiteSpace(identityName)
                ? normalizedCategory
                : PersonalMemoryItem.CategoryPreference;
            var finalContent = string.IsNullOrWhiteSpace(identityName)
                ? trimmed
                : $"nama saya {identityName}";
            var normalizedContent = Normalize(finalContent);

            var exact = doc.Items.FirstOrDefault(x =>
                !x.IsDeleted
                && string.Equals(x.Category, finalCategory, StringComparison.Ordinal)
                && string.Equals(Normalize(x.Content), normalizedContent, StringComparison.Ordinal));
            if (exact is not null)
                return exact;

            if (!string.IsNullOrWhiteSpace(identityName))
            {
                for (var i = 0; i < doc.Items.Count; i++)
                {
                    var item = doc.Items[i];
                    if (item.IsDeleted)
                        continue;

                    if (!IsIdentityNameMemory(item))
                        continue;

                    doc.Items[i] = item with
                    {
                        IsDeleted = true,
                        UpdatedUtc = nowUtc
                    };
                }
            }

            var created = new PersonalMemoryItem
            {
                MemoryId = Guid.NewGuid().ToString("N"),
                Category = finalCategory,
                Content = finalContent,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc,
                IsDeleted = false
            }.Normalize();

            doc.Items.Add(created);
            await WriteDocumentInternalAsync(doc, ct);
            return created;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<string> GetLatestIdentityNameAsync(CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadDocumentInternalAsync(ct);
            var latest = doc.Items
                .Where(x => !x.IsDeleted && IsIdentityNameMemory(x))
                .OrderByDescending(x => x.UpdatedUtc)
                .FirstOrDefault();

            if (latest is null)
                return string.Empty;

            return TryExtractIdentityName(latest.Content, true) ?? string.Empty;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<PersonalMemoryItem> UpdateAsync(string memoryId, string category, string content, CancellationToken ct)
    {
        if (!PersonalMemoryItem.IsSafeMemoryId(memoryId))
            throw new ArgumentException("Memory id is invalid.", nameof(memoryId));

        var trimmed = (content ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Content is required.", nameof(content));

        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadDocumentInternalAsync(ct);
            var index = doc.Items.FindIndex(x => string.Equals(x.MemoryId, memoryId, StringComparison.Ordinal) && !x.IsDeleted);
            if (index < 0)
                throw new KeyNotFoundException($"Memory item '{memoryId}' was not found.");

            var current = doc.Items[index];
            var updated = current with
            {
                Category = PersonalMemoryItem.NormalizeCategory(category),
                Content = trimmed,
                UpdatedUtc = DateTimeOffset.UtcNow,
                IsDeleted = false
            };

            doc.Items[index] = updated.Normalize();
            await WriteDocumentInternalAsync(doc, ct);
            return doc.Items[index];
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<bool> DeleteAsync(string memoryId, CancellationToken ct)
    {
        if (!PersonalMemoryItem.IsSafeMemoryId(memoryId))
            throw new ArgumentException("Memory id is invalid.", nameof(memoryId));

        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadDocumentInternalAsync(ct);
            var index = doc.Items.FindIndex(x => string.Equals(x.MemoryId, memoryId, StringComparison.Ordinal) && !x.IsDeleted);
            if (index < 0)
                return false;

            var deleted = doc.Items[index] with
            {
                IsDeleted = true,
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            doc.Items[index] = deleted.Normalize();
            await WriteDocumentInternalAsync(doc, ct);
            return true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<PersonalMemoryItem>> GetRelevantAsync(string query, CancellationToken ct)
    {
        var queryTokens = UniqueTokens(query);
        if (queryTokens.Count == 0)
            return Array.Empty<PersonalMemoryItem>();

        var identityQuery = IsIdentityQuery(queryTokens);

        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadDocumentInternalAsync(ct);
            var scored = doc.Items
                .Where(x => !x.IsDeleted)
                .Select(x => new
                {
                    Item = x,
                    Score = ComputeScore(x, queryTokens, identityQuery)
                })
                .Where(x => x.Score >= 2)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Item.UpdatedUtc)
                .Select(x => x.Item)
                .ToList();

            var capped = new List<PersonalMemoryItem>(MaxRelevantItems);
            var totalChars = 0;
            foreach (var item in scored)
            {
                if (capped.Count >= MaxRelevantItems)
                    break;

                var contentLength = item.Content?.Length ?? 0;
                if (capped.Count > 0 && totalChars + contentLength > MaxRelevantContentChars)
                    break;

                capped.Add(item);
                totalChars += contentLength;
            }

            return capped;
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<PersonalMemoryDocument> ReadDocumentInternalAsync(CancellationToken ct)
    {
        var filePath = GetFilePath();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(filePath))
        {
            return new PersonalMemoryDocument();
        }

        try
        {
            var raw = await File.ReadAllTextAsync(filePath, ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new PersonalMemoryDocument();
            }

            var parsed = JsonSerializer.Deserialize<PersonalMemoryDocument>(raw);
            return (parsed ?? new PersonalMemoryDocument()).Normalize();
        }
        catch
        {
            return new PersonalMemoryDocument();
        }
    }

    private async Task WriteDocumentInternalAsync(PersonalMemoryDocument doc, CancellationToken ct)
    {
        var normalized = (doc ?? new PersonalMemoryDocument()).Normalize();
        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        await File.WriteAllTextAsync(GetFilePath(), json, ct);
    }

    private static string BuildHaystack(PersonalMemoryItem item)
    {
        var merged = string.Concat(item.Category, " ", item.Content);
        return Normalize(merged);
    }

    private static List<string> UniqueTokens(string value)
    {
        return Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static int CountOverlap(string haystack, IReadOnlyCollection<string> tokens)
    {
        var tokenSet = haystack
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);

        var count = 0;
        foreach (var token in tokens)
        {
            if (tokenSet.Contains(token))
                count++;
        }

        return count;
    }

    private static int ComputeScore(PersonalMemoryItem item, IReadOnlyCollection<string> queryTokens, bool identityQuery)
    {
        var overlap = CountOverlap(BuildHaystack(item), queryTokens);
        if (!identityQuery)
            return overlap;

        return overlap + (LooksLikeIdentityMemory(item) ? 2 : 0);
    }

    private static bool IsIdentityQuery(IReadOnlyCollection<string> queryTokens)
    {
        if (queryTokens.Count == 0)
            return false;

        var set = queryTokens.ToHashSet(StringComparer.Ordinal);
        var hasNameToken = set.Contains("nama") || set.Contains("name") || set.Contains("siapa") || set.Contains("panggil");
        var hasSelfToken = set.Contains("saya") || set.Contains("aku") || set.Contains("gua") || set.Contains("gue") || set.Contains("gw");
        return hasNameToken && hasSelfToken;
    }

    private static bool LooksLikeIdentityMemory(PersonalMemoryItem item)
    {
        if (!string.Equals(item.Category, PersonalMemoryItem.CategoryPreference, StringComparison.Ordinal))
            return false;

        var normalized = Normalize(item.Content);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.Contains("github", StringComparison.Ordinal)
            || normalized.Contains("repo", StringComparison.Ordinal)
            || normalized.Contains("project", StringComparison.Ordinal)
            || normalized.Contains("http", StringComparison.Ordinal)
            || normalized.Contains("www", StringComparison.Ordinal)
            || normalized.Contains('@'))
        {
            return false;
        }

        if (normalized.Contains("nama", StringComparison.Ordinal)
            || normalized.Contains("name", StringComparison.Ordinal)
            || normalized.Contains("panggil", StringComparison.Ordinal))
        {
            return true;
        }

        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 2)
            .ToList();

        if (tokens.Count < 2 || tokens.Count > 4)
            return false;

        foreach (var token in tokens)
        {
            if (token.Any(ch => ch < 'a' || ch > 'z'))
                return false;
        }

        return true;
    }

    private static string Normalize(string value)
    {
        var text = (value ?? string.Empty).ToLowerInvariant();
        var sb = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append(' ');
            }
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsIdentityNameMemory(PersonalMemoryItem item)
        => !string.IsNullOrWhiteSpace(TryExtractIdentityName(item.Content, true));

    private static string TryExtractIdentityName(string content, bool allowBareName)
    {
        var normalized = Normalize(content);
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var markers = new[]
        {
            "nama saya",
            "nama gua",
            "nama gue",
            "nama aku",
            "name saya",
            "my name is",
            "my name",
            "nama ",
            "name "
        };

        foreach (var marker in markers)
        {
            var idx = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                continue;

            var tail = normalized[(idx + marker.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(tail))
                return tail;
        }

        if (!allowBareName)
            return string.Empty;

        if (normalized.Contains("github", StringComparison.Ordinal)
            || normalized.Contains("repo", StringComparison.Ordinal)
            || normalized.Contains("http", StringComparison.Ordinal)
            || normalized.Contains("www", StringComparison.Ordinal)
            || normalized.Contains("project", StringComparison.Ordinal)
            || normalized.Contains("kerja", StringComparison.Ordinal)
            || normalized.Contains("developer", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 3)
            return string.Empty;

        return normalized;

    }
}
