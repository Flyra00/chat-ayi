using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatAyi.Services;

public sealed class LocalSessionStore
{
    private const string SessionIdKey = "ChatAyi.SessionId";
    private static readonly Regex SafeId = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);

    private sealed record ChatLine(string role, string content);

    public string GetOrCreateSessionId()
    {
        var existing = Preferences.Get(SessionIdKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing) && SafeId.IsMatch(existing))
            return existing;

        var id = Guid.NewGuid().ToString("N");
        Preferences.Set(SessionIdKey, id);
        return id;
    }

    public string GetSessionsRoot()
        => Path.Combine(FileSystem.AppDataDirectory, "sessions");

    public string GetSessionFilePath(string sessionId)
    {
        Directory.CreateDirectory(GetSessionsRoot());
        return Path.Combine(GetSessionsRoot(), $"{sessionId}.jsonl");
    }

    public async Task AppendAsync(string sessionId, object record, CancellationToken ct)
    {
        var file = GetSessionFilePath(sessionId);
        var line = JsonSerializer.Serialize(record) + "\n";
        await File.AppendAllTextAsync(file, line, ct);
    }

    public async Task<List<(string Role, string Content)>> ReadRecentChatAsync(string sessionId, int maxMessages, CancellationToken ct)
    {
        maxMessages = Math.Clamp(maxMessages, 1, 200);

        var file = GetSessionFilePath(sessionId);
        if (!File.Exists(file)) return new List<(string, string)>();

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(file, ct);
        }
        catch
        {
            return new List<(string, string)>();
        }

        var outLines = new List<(string Role, string Content)>(maxMessages);
        for (var i = lines.Length - 1; i >= 0 && outLines.Count < maxMessages; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var parsed = JsonSerializer.Deserialize<ChatLine>(line);
                if (parsed is null) continue;
                if (parsed.role is not ("user" or "assistant")) continue;
                if (string.IsNullOrWhiteSpace(parsed.content)) continue;
                outLines.Add((parsed.role, parsed.content));
            }
            catch
            {
                // ignore
            }
        }

        outLines.Reverse();
        return outLines;
    }
}
