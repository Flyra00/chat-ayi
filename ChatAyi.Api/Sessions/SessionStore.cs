using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatAyi.Api.Sessions;

public sealed class SessionStore
{
    private static readonly Regex SafeId = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);

    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;

    private sealed record ChatLine(string role, string content);

    public SessionStore(IHostEnvironment env, IConfiguration config)
    {
        _env = env;
        _config = config;
    }

    public string GetSessionsRoot()
    {
        var sessionsDir = _config["Sessions:Directory"] ?? "sessions";
        return Path.IsPathRooted(sessionsDir) ? sessionsDir : Path.Combine(_env.ContentRootPath, sessionsDir);
    }

    public string GetSessionFilePath(string sessionId)
    {
        var root = GetSessionsRoot();
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"{sessionId}.jsonl");
    }

    public string GetSessionId(HttpContext ctx)
    {
        var raw = ctx.Request.Headers["X-ChatAyi-SessionId"].ToString();
        if (!string.IsNullOrWhiteSpace(raw) && SafeId.IsMatch(raw))
            return raw;

        return "main";
    }

    public async Task AppendAsync(string sessionId, object record, CancellationToken ct)
    {
        var file = GetSessionFilePath(sessionId);
        var line = JsonSerializer.Serialize(record) + "\n";
        await File.AppendAllTextAsync(file, line, ct);
    }

    public async Task<List<(string Role, string Content)>> ReadRecentChatAsync(
        string sessionId,
        int maxMessages,
        CancellationToken ct)
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
