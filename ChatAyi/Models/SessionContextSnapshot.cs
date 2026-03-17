using System.Text.Json.Serialization;

namespace ChatAyi.Models;

public sealed record SessionContextSnapshot
{
    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("recent_turns")]
    public List<SessionTurn> RecentTurns { get; init; } = new();

    [JsonPropertyName("summary_bullets")]
    public List<string> SummaryBullets { get; init; } = new();

    public static SessionContextSnapshot Create(
        string sessionId,
        IEnumerable<(string Role, string Content)> recentTurns,
        IEnumerable<string> summaryBullets)
    {
        var turns = (recentTurns ?? Enumerable.Empty<(string Role, string Content)>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Role) && !string.IsNullOrWhiteSpace(x.Content))
            .TakeLast(6)
            .Select(x => new SessionTurn(x.Role.Trim().ToLowerInvariant(), x.Content.Trim()))
            .ToList();

        var bullets = (summaryBullets ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Take(5)
            .ToList();

        return new SessionContextSnapshot
        {
            SessionId = SessionMeta.IsSafeSessionId(sessionId) ? sessionId : string.Empty,
            RecentTurns = turns,
            SummaryBullets = bullets
        };
    }
}

public sealed record SessionTurn(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);
