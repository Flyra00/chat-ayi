using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ChatAyi.Models;

public sealed record SessionMeta
{
    private static readonly Regex SafeSessionIdRegex = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);
    private const int SelectorTitleMaxLength = 36;

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = "New Session";

    [JsonPropertyName("last_activity_utc")]
    public DateTimeOffset LastActivityUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string SelectorLabel
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "New Session" : Title.Trim();
            if (title.Length > SelectorTitleMaxLength)
                title = title[..SelectorTitleMaxLength].TrimEnd() + "...";

            return $"{title} ({LastActivityUtc.ToLocalTime():dd MMM HH:mm})";
        }
    }

    public SessionMeta Normalize()
        => new()
        {
            SessionId = IsSafeSessionId(SessionId) ? SessionId : Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(Title) ? "New Session" : Title.Trim(),
            LastActivityUtc = LastActivityUtc == default ? DateTimeOffset.UtcNow : LastActivityUtc.ToUniversalTime()
        };

    public static bool IsSafeSessionId(string sessionId)
        => !string.IsNullOrWhiteSpace(sessionId) && SafeSessionIdRegex.IsMatch(sessionId);
}
