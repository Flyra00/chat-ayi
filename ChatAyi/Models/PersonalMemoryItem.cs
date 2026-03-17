using System.Text.Json.Serialization;

namespace ChatAyi.Models;

public sealed record PersonalMemoryItem
{
    public const string CategoryPreference = "preference";
    public const string CategoryActiveProject = "active_project";
    public const string CategoryImportantInfo = "important_info";

    [JsonPropertyName("memory_id")]
    public string MemoryId { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = CategoryPreference;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }

    [JsonPropertyName("is_deleted")]
    public bool IsDeleted { get; init; }

    public static bool IsSafeMemoryId(string memoryId)
    {
        if (string.IsNullOrWhiteSpace(memoryId))
            return false;

        foreach (var ch in memoryId)
        {
            if ((ch >= 'a' && ch <= 'z')
                || (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch == '-'
                || ch == '_')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    public PersonalMemoryItem Normalize()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var createdUtc = NormalizeUtc(CreatedUtc, nowUtc);
        var updatedUtc = NormalizeUtc(UpdatedUtc, createdUtc);

        if (updatedUtc < createdUtc)
            updatedUtc = createdUtc;

        return new PersonalMemoryItem
        {
            MemoryId = IsSafeMemoryId(MemoryId) ? MemoryId : Guid.NewGuid().ToString("N"),
            Category = NormalizeCategory(Category),
            Content = (Content ?? string.Empty).Trim(),
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc,
            IsDeleted = IsDeleted
        };
    }

    public static string NormalizeCategory(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

        if (normalized == CategoryPreference
            || normalized == CategoryActiveProject
            || normalized == CategoryImportantInfo)
        {
            return normalized;
        }

        if (normalized.Contains("project", StringComparison.Ordinal)
            || normalized.Contains("proyek", StringComparison.Ordinal)
            || normalized.Contains("task", StringComparison.Ordinal)
            || normalized.Contains("milestone", StringComparison.Ordinal)
            || normalized.Contains("kerja", StringComparison.Ordinal))
        {
            return CategoryActiveProject;
        }

        if (normalized.Contains("important", StringComparison.Ordinal)
            || normalized.Contains("penting", StringComparison.Ordinal)
            || normalized.Contains("critical", StringComparison.Ordinal)
            || normalized.Contains("urgent", StringComparison.Ordinal)
            || normalized.Contains("info", StringComparison.Ordinal))
        {
            return CategoryImportantInfo;
        }

        if (normalized.Contains("preference", StringComparison.Ordinal)
            || normalized.Contains("prefer", StringComparison.Ordinal)
            || normalized.Contains("suka", StringComparison.Ordinal)
            || normalized.Contains("habit", StringComparison.Ordinal)
            || normalized.Contains("style", StringComparison.Ordinal)
            || normalized.Contains("gaya", StringComparison.Ordinal))
        {
            return CategoryPreference;
        }

        return CategoryPreference;
    }

    private static DateTimeOffset NormalizeUtc(DateTimeOffset value, DateTimeOffset fallbackUtc)
        => value == default
            ? fallbackUtc
            : value.ToUniversalTime();
}
