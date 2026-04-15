using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace ChatAyi.Services;

public sealed class SearxngSearchClient
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<string> _instancePool;
    private readonly TimeSpan _perInstanceTimeout;

    /// <summary>
    /// Default SearXNG instance pool. These are public volunteer-maintained instances
    /// selected for relative stability and geographic proximity (Asia-friendly).
    /// Order matters — the first instance is tried first, then fallbacks in sequence.
    /// </summary>
    private static readonly string[] DefaultInstancePool =
    {
        "https://searx.be",
        "https://search.sapti.me",
        "https://searxng.site",
        "https://search.bus-hit.me",
        "https://priv.au",
        "https://searx.tiekoetter.com",
        "https://search.ononoki.org",
    };

    public SearxngSearchClient(HttpClient http, string baseUrl)
        : this(http, baseUrl, fallbackInstances: null, perInstanceTimeout: TimeSpan.FromSeconds(8))
    {
    }

    public SearxngSearchClient(
        HttpClient http,
        string baseUrl,
        IReadOnlyList<string> fallbackInstances,
        TimeSpan perInstanceTimeout)
    {
        _http = http;
        _perInstanceTimeout = perInstanceTimeout.TotalSeconds > 0 ? perInstanceTimeout : TimeSpan.FromSeconds(8);

        // Build ordered instance pool: user-specified primary first, then fallbacks.
        var pool = new List<string>();
        var primary = NormalizeBaseUrl(baseUrl);
        pool.Add(primary);

        // Add user-provided fallback instances (from env var or constructor).
        if (fallbackInstances is { Count: > 0 })
        {
            foreach (var fb in fallbackInstances)
            {
                var normalized = NormalizeBaseUrl(fb);
                if (!pool.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    pool.Add(normalized);
            }
        }

        // If pool is still just one instance, backfill from default pool (skip duplicates).
        if (pool.Count < 3)
        {
            foreach (var defaultInstance in DefaultInstancePool)
            {
                var normalized = NormalizeBaseUrl(defaultInstance);
                if (!pool.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    pool.Add(normalized);
            }
        }

        _instancePool = pool.AsReadOnly();
        Debug.WriteLine($"[SearXNG] Pool initialized with {_instancePool.Count} instances: {string.Join(", ", _instancePool)}");
    }

    public sealed record SearchResult(string Title, string Url, string Snippet);

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return new List<SearchResult>();

        maxResults = Math.Clamp(maxResults, 1, 10);

        Exception lastException = null;

        for (var i = 0; i < _instancePool.Count; i++)
        {
            var instanceUrl = _instancePool[i];
            var instanceNumber = i + 1;

            Debug.WriteLine($"[SearXNG] Trying instance #{instanceNumber}/{_instancePool.Count}: {instanceUrl}");

            try
            {
                var results = await TrySearchInstanceAsync(instanceUrl, query, maxResults, ct);

                if (results.Count > 0)
                {
                    Debug.WriteLine($"[SearXNG] Instance #{instanceNumber} succeeded: {results.Count} results from {instanceUrl}");
                    return results;
                }

                // Instance returned 0 results — not an error, but try next for better coverage.
                Debug.WriteLine($"[SearXNG] Instance #{instanceNumber} returned 0 results, trying next...");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // User-initiated cancellation — don't retry, just bail.
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                var reason = ex is OperationCanceledException ? "timeout" : ex.Message;
                Debug.WriteLine($"[SearXNG] Instance #{instanceNumber} failed ({reason}), trying next...");
            }
        }

        // All instances exhausted.
        Debug.WriteLine($"[SearXNG] All {_instancePool.Count} instances failed/empty.");

        if (lastException is not null)
            throw new HttpRequestException(
                $"All {_instancePool.Count} SearXNG instances failed. Last error: {lastException.Message}",
                lastException);

        return new List<SearchResult>();
    }

    private async Task<List<SearchResult>> TrySearchInstanceAsync(
        string baseUrl, string query, int maxResults, CancellationToken ct)
    {
        var url = baseUrl + "/search?q=" + Uri.EscapeDataString(query) + "&format=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        // Per-instance timeout — don't let one slow instance block the whole pool.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_perInstanceTimeout);

        HttpResponseMessage resp;
        string text;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            text = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException($"SearXNG instance timed out after {_perInstanceTimeout.TotalSeconds}s: {baseUrl}");
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"SearXNG HTTP {(int)resp.StatusCode} from {baseUrl}" +
                    (string.IsNullOrWhiteSpace(text) ? string.Empty : ": " + text.Substring(0, Math.Min(200, text.Length))));
        }

        // Derive host for self-link filtering.
        var instanceHost = Uri.TryCreate(baseUrl, UriKind.Absolute, out var instanceUri)
            ? instanceUri.Host
            : string.Empty;

        return ParseSearchResults(text, instanceHost, maxResults);
    }

    private static List<SearchResult> ParseSearchResults(string json, string instanceHost, int maxResults)
    {
        var results = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var item in arr.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? (t.GetString() ?? string.Empty).Trim()
                : string.Empty;

            var link = item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                ? (u.GetString() ?? string.Empty).Trim()
                : string.Empty;

            var snippet = item.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? (c.GetString() ?? string.Empty).Trim()
                : string.Empty;

            if (snippet.Length == 0 && item.TryGetProperty("snippet", out var s) && s.ValueKind == JsonValueKind.String)
                snippet = (s.GetString() ?? string.Empty).Trim();

            if (link.Length == 0) continue;
            if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme is not ("http" or "https")) continue;
            if (!string.IsNullOrWhiteSpace(instanceHost) && string.Equals(uri.Host, instanceHost, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(uri.ToString())) continue;

            results.Add(new SearchResult(title.Length > 0 ? title : link, link, snippet));
            if (results.Count >= maxResults) break;
        }

        return results;
    }

    /// <summary>
    /// Parses a semicolon or comma separated string of SearXNG instance URLs.
    /// Used for the CHATAYI_SEARXNG_FALLBACK_INSTANCES environment variable.
    /// </summary>
    public static List<string> ParseFallbackInstancesEnvVar(string envValue)
    {
        if (string.IsNullOrWhiteSpace(envValue))
            return new List<string>();

        return envValue
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            .ToList();
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var value = (baseUrl ?? string.Empty).Trim();
        if (value.Length == 0)
            value = "https://searx.be";

        return value.TrimEnd('/');
    }
}
