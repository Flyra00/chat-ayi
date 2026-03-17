using System.Text.Json;
using System.Text.RegularExpressions;
using ChatAyi.Models;

namespace ChatAyi.Services;

public sealed class LocalSessionStore
{
    private const string SessionIdKey = "ChatAyi.SessionId";
    private static readonly Regex SafeId = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public string GetOrCreateSessionId()
    {
        var existing = Preferences.Get(SessionIdKey, string.Empty);
        if (IsSafeSessionId(existing))
            return existing;

        var id = Guid.NewGuid().ToString("N");
        Preferences.Set(SessionIdKey, id);
        return id;
    }

    public string GetSessionsRoot()
        => Path.Combine(FileSystem.AppDataDirectory, "sessions");

    public string GetSessionFilePath(string sessionId)
    {
        sessionId = NormalizeSessionId(sessionId);
        Directory.CreateDirectory(GetSessionsRoot());
        return Path.Combine(GetSessionsRoot(), $"{sessionId}.jsonl");
    }

    public async Task AppendAsync(string sessionId, object record, CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var file = GetSessionFilePath(sessionId);
            var line = JsonSerializer.Serialize(record) + "\n";
            await File.AppendAllTextAsync(file, line, ct);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<DateTimeOffset> AppendWithMetadataAsync(
        string sessionId,
        object record,
        SessionCatalogStore catalogStore,
        string title,
        CancellationToken ct)
    {
        if (catalogStore is null)
            throw new ArgumentNullException(nameof(catalogStore));

        sessionId = NormalizeSessionId(sessionId);
        var activityUtc = DateTimeOffset.UtcNow;

        await AppendAsync(sessionId, record, ct);
        await catalogStore.TouchAsync(sessionId, title, activityUtc, ct);
        return activityUtc;
    }

    public sealed record SessionTranscriptEntry(DateTimeOffset? TimestampUtc, string Role, string Content, string Model);

    public async Task<List<SessionTranscriptEntry>> ReadTranscriptAsync(string sessionId, CancellationToken ct)
    {
        sessionId = NormalizeSessionId(sessionId);
        var file = GetSessionFilePath(sessionId);
        if (!File.Exists(file))
            return new List<SessionTranscriptEntry>();

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(file, ct);
        }
        catch
        {
            return new List<SessionTranscriptEntry>();
        }

        var entries = new List<SessionTranscriptEntry>(lines.Length);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!TryParseTranscriptLine(line, out var entry))
                continue;

            entries.Add(entry);
        }

        return entries;
    }

    public async Task<List<(string Role, string Content)>> ReadRecentChatAsync(string sessionId, int maxMessages, CancellationToken ct)
    {
        maxMessages = Math.Clamp(maxMessages, 1, 200);

        var transcript = await ReadTranscriptAsync(sessionId, ct);
        return transcript
            .Where(x => x.Role is "user" or "assistant")
            .TakeLast(maxMessages)
            .Select(x => (x.Role, x.Content))
            .ToList();
    }

    public static bool IsSafeSessionId(string sessionId)
        => !string.IsNullOrWhiteSpace(sessionId) && SafeId.IsMatch(sessionId);

    private static string NormalizeSessionId(string sessionId)
    {
        if (!IsSafeSessionId(sessionId))
            throw new ArgumentException("Session id is invalid.", nameof(sessionId));

        return sessionId;
    }

    private static bool TryParseTranscriptLine(string line, out SessionTranscriptEntry entry)
    {
        entry = null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("role", out var roleNode))
                return false;

            if (!root.TryGetProperty("content", out var contentNode))
                return false;

            var role = (roleNode.GetString() ?? string.Empty).Trim().ToLowerInvariant();
            var content = (contentNode.GetString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(content))
                return false;

            DateTimeOffset? timestamp = null;
            if (root.TryGetProperty("ts", out var tsNode) && tsNode.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(tsNode.GetString(), out var parsedTs))
                    timestamp = parsedTs.ToUniversalTime();
            }

            var model = string.Empty;
            if (root.TryGetProperty("model", out var modelNode) && modelNode.ValueKind == JsonValueKind.String)
                model = modelNode.GetString() ?? string.Empty;

            entry = new SessionTranscriptEntry(timestamp, role, content, model);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
