using System.Text.Json;
using ChatAyi.Models;

namespace ChatAyi.Services;

public sealed class SessionCatalogStore
{
    private const string CatalogFileName = "session-catalog.json";
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private sealed class CatalogDocument
    {
        public string ActiveSessionId { get; set; }
        public List<SessionMeta> Sessions { get; set; } = new();
    }

    public string GetCatalogFilePath()
        => Path.Combine(FileSystem.AppDataDirectory, CatalogFileName);

    public async Task<IReadOnlyList<SessionMeta>> ListAsync(CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadCatalogInternalAsync(ct);
            return doc.Sessions
                .Select(x => x.Normalize())
                .OrderByDescending(x => x.LastActivityUtc)
                .ToList();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<SessionMeta> UpsertAsync(SessionMeta meta, CancellationToken ct)
    {
        var normalized = (meta ?? new SessionMeta()).Normalize();

        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadCatalogInternalAsync(ct);
            var index = doc.Sessions.FindIndex(x => string.Equals(x.SessionId, normalized.SessionId, StringComparison.Ordinal));
            if (index >= 0)
            {
                doc.Sessions[index] = normalized;
            }
            else
            {
                doc.Sessions.Add(normalized);
            }

            await WriteCatalogInternalAsync(doc, ct);
            return normalized;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<SessionMeta> TouchAsync(string sessionId, string title, DateTimeOffset? activityUtc, CancellationToken ct)
    {
        if (!SessionMeta.IsSafeSessionId(sessionId))
            throw new ArgumentException("Session id is invalid.", nameof(sessionId));

        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadCatalogInternalAsync(ct);
            var now = (activityUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
            var index = doc.Sessions.FindIndex(x => string.Equals(x.SessionId, sessionId, StringComparison.Ordinal));

            SessionMeta updated;
            if (index >= 0)
            {
                var current = doc.Sessions[index].Normalize();
                updated = current with
                {
                    Title = string.IsNullOrWhiteSpace(title) ? current.Title : title.Trim(),
                    LastActivityUtc = now
                };
                doc.Sessions[index] = updated;
            }
            else
            {
                updated = new SessionMeta
                {
                    SessionId = sessionId,
                    Title = string.IsNullOrWhiteSpace(title) ? "New Session" : title.Trim(),
                    LastActivityUtc = now
                }.Normalize();

                doc.Sessions.Add(updated);
            }

            await WriteCatalogInternalAsync(doc, ct);
            return updated;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<string> GetActiveSessionIdAsync(CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadCatalogInternalAsync(ct);
            return SessionMeta.IsSafeSessionId(doc.ActiveSessionId) ? doc.ActiveSessionId : string.Empty;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SetActiveSessionIdAsync(string sessionId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(sessionId) && !SessionMeta.IsSafeSessionId(sessionId))
            throw new ArgumentException("Session id is invalid.", nameof(sessionId));

        await _mutex.WaitAsync(ct);
        try
        {
            var doc = await ReadCatalogInternalAsync(ct);
            doc.ActiveSessionId = string.IsNullOrWhiteSpace(sessionId) ? string.Empty : sessionId;
            await WriteCatalogInternalAsync(doc, ct);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<CatalogDocument> ReadCatalogInternalAsync(CancellationToken ct)
    {
        var filePath = GetCatalogFilePath();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(filePath))
        {
            return new CatalogDocument();
        }

        try
        {
            var raw = await File.ReadAllTextAsync(filePath, ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new CatalogDocument();
            }

            var parsed = JsonSerializer.Deserialize<CatalogDocument>(raw);
            return parsed ?? new CatalogDocument();
        }
        catch
        {
            return new CatalogDocument();
        }
    }

    private async Task WriteCatalogInternalAsync(CatalogDocument doc, CancellationToken ct)
    {
        var filePath = GetCatalogFilePath();
        var payload = new CatalogDocument
        {
            ActiveSessionId = SessionMeta.IsSafeSessionId(doc.ActiveSessionId) ? doc.ActiveSessionId : string.Empty,
            Sessions = doc.Sessions
                .Select(x => x.Normalize())
                .GroupBy(x => x.SessionId, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(x => x.LastActivityUtc).First())
                .OrderByDescending(x => x.LastActivityUtc)
                .ToList()
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(payload, options), ct);
    }
}
