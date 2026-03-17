using System.Text;
using System.Text.Json;
using ChatAyi.Models;

namespace ChatAyi.Services;

public sealed class PersonalMemoryStore
{
    private const string MemoryFileName = "personal-memory.json";
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

            var created = new PersonalMemoryItem
            {
                MemoryId = Guid.NewGuid().ToString("N"),
                Category = PersonalMemoryItem.NormalizeCategory(category),
                Content = trimmed,
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

        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadDocumentInternalAsync(ct);
            var scored = doc.Items
                .Where(x => !x.IsDeleted)
                .Select(x => new
                {
                    Item = x,
                    MatchCount = CountOverlap(BuildHaystack(x), queryTokens)
                })
                .Where(x => x.MatchCount >= 2)
                .OrderByDescending(x => x.MatchCount)
                .ThenByDescending(x => x.Item.UpdatedUtc)
                .Take(5)
                .Select(x => x.Item)
                .ToList();

            return scored;
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
}
