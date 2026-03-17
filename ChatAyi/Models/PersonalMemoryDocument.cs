using System.Text.Json.Serialization;

namespace ChatAyi.Models;

public sealed record PersonalMemoryDocument
{
    [JsonPropertyName("items")]
    public List<PersonalMemoryItem> Items { get; init; } = new();

    public PersonalMemoryDocument Normalize()
    {
        var normalized = (Items ?? new List<PersonalMemoryItem>())
            .Select(x => (x ?? new PersonalMemoryItem()).Normalize())
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .GroupBy(x => x.MemoryId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.UpdatedUtc).First())
            .OrderByDescending(x => x.UpdatedUtc)
            .ToList();

        return new PersonalMemoryDocument
        {
            Items = normalized
        };
    }
}
