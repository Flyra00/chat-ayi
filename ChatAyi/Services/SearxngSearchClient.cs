using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace ChatAyi.Services;

public sealed class SearxngSearchClient
{
    private readonly HttpClient _http;
    private readonly TimeSpan _perInstanceTimeout;
    private readonly TimeSpan _globalSearchTimeout;

    // ── Runtime-mutable instance pool (updated via Settings UI) ─────────
    private IReadOnlyList<string> _instancePool;
    private string _customBaseUrl;
    private bool _userSetCustomUrl;
    private readonly object _poolLock = new();
    private readonly IReadOnlyList<string> _fallbackInstances;

    // ── Dead instance tracking ────────────────────────────────────────
    // Instances that recently failed are skipped to avoid wasting time.
    // They're retried after the expiry window.
    private static readonly ConcurrentDictionary<string, DateTime> _deadInstances = new();
    private static readonly TimeSpan DeadInstanceRetryAfter = TimeSpan.FromSeconds(60);

    // Default concurrency: probe up to 3 instances in parallel per batch.
    private const int BatchSize = 3;

    /// <summary>
    /// Default SearXNG instance pool. These are public volunteer-maintained instances
    /// sourced from searx.space (confirmed online with HTTP 200 as of May 2026).
    /// Rotated away from overloaded instances that commonly block datacenter IPs.
    /// </summary>
    private static readonly string[] DefaultInstancePool =
    {
        "https://searxng.cups.moe",
        "https://searxng.canine.tools",
        "https://searx.sev.monster",
        "https://search.bladerunn.in",
        "https://sx.catgirl.cloud",
        "https://grep.vim.wtf",
        "https://search.internetsucks.net",
        "https://search.minus27315.dev",
        "https://etsi.me",
        "https://kantan.cat",
        "https://copp.gg",
        "https://search.ctq.ro",
        "https://searx.oloke.xyz",
        "https://searx.namejeff.xyz",
    };

    public SearxngSearchClient(HttpClient http, string baseUrl)
        : this(http, baseUrl, fallbackInstances: null, perInstanceTimeout: TimeSpan.FromSeconds(5))
    {
    }

    public SearxngSearchClient(
        HttpClient http,
        string baseUrl,
        IReadOnlyList<string> fallbackInstances,
        TimeSpan perInstanceTimeout)
    {
        _http = http;
        _perInstanceTimeout = perInstanceTimeout.TotalSeconds > 0 ? perInstanceTimeout : TimeSpan.FromSeconds(5);
        _globalSearchTimeout = TimeSpan.FromSeconds(Math.Max(_perInstanceTimeout.TotalSeconds * 3, 15));
        _fallbackInstances = fallbackInstances ?? Array.Empty<string>();
        _customBaseUrl = NormalizeBaseUrl(baseUrl);
        _userSetCustomUrl = !string.IsNullOrWhiteSpace(baseUrl);

        RebuildPool();
    }

    /// <summary>
    /// Updates the primary SearXNG URL at runtime (called from Settings UI).
    /// Clears dead-instance tracking so the new URL gets a fair try.
    /// </summary>
    public void SetCustomBaseUrl(string url)
    {
        lock (_poolLock)
        {
            _customBaseUrl = NormalizeBaseUrl(url);
            _userSetCustomUrl = !string.IsNullOrWhiteSpace(url);
            _deadInstances.Clear();
            RebuildPool();
        }
        Debug.WriteLine($"[SearXNG] Custom URL updated: {_customBaseUrl}; userSet={_userSetCustomUrl} pool={_instancePool.Count} instances");
    }

    private void RebuildPool()
    {
        var pool = new List<string>();
        pool.Add(_customBaseUrl);

        // Add user-provided fallback instances (from env var or constructor).
        if (_fallbackInstances.Count > 0)
        {
            foreach (var fb in _fallbackInstances)
            {
                var normalized = NormalizeBaseUrl(fb);
                if (!pool.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    pool.Add(normalized);
            }
        }

        // If pool still has very few instances and no user-specified custom URL,
        // backfill with public defaults so we have a reasonable number of candidates.
        if (!_userSetCustomUrl && pool.Count < 3)
        {
            foreach (var defaultInstance in DefaultInstancePool)
            {
                var normalized = NormalizeBaseUrl(defaultInstance);
                if (!pool.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    pool.Add(normalized);
            }
        }

        _instancePool = pool.AsReadOnly();
    }

    public sealed record SearchResult(string Title, string Url, string Snippet);

    /// <summary>
    /// Searches across the instance pool using batched parallel probing.
    /// Instances are tried in batches of 3 in parallel. The first batch with
    /// a successful response wins. Dead instances (recently failed) are skipped.
    /// A global timeout bounds the entire SearXNG phase.
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return new List<SearchResult>();
        maxResults = Math.Clamp(maxResults, 1, 10);

        // Build active list: skip recently-dead instances, reset if all are dead.
        var now = DateTime.UtcNow;
        var active = _instancePool
            .Where(url => !_deadInstances.TryGetValue(url, out var deadUntil) || deadUntil <= now)
            .ToList();
        if (active.Count == 0)
        {
            // All instances are marked dead — reset and try everything.
            _deadInstances.Clear();
            active = _instancePool.ToList();
            Debug.WriteLine("[SearXNG] All instances were dead — resetting death marks.");
        }

        // Global SearXNG phase timeout so the user doesn't wait forever.
        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        globalCts.CancelAfter(_globalSearchTimeout);
        var globalToken = globalCts.Token;

        // Log which instances we're probing.
        var skippedCount = _instancePool.Count - active.Count;
        if (skippedCount > 0)
            Debug.WriteLine($"[SearXNG] Skipping {skippedCount} recently-dead instances, probing {active.Count} active ones.");

        var attemptCount = 0;
        while (active.Count > 0)
        {
            // Take the next batch
            var batch = active.Take(BatchSize).ToList();
            active = active.Skip(BatchSize).ToList();
            attemptCount += batch.Count;

            var batchResult = await TryProbeBatchAsync(batch, query, maxResults, globalToken);

            if (batchResult is not null)
            {
                // First successful batch wins.
                Debug.WriteLine($"[SearXNG] Batch got {batchResult.Count} results after {attemptCount} instances tried");
                return batchResult;
            }

            // Batch failed — try next batch.
            Debug.WriteLine($"[SearXNG] Batch failed ({batch.Count} instances), trying next batch...");

            if (globalToken.IsCancellationRequested)
                break;
        }

        // All batches exhausted or timed out.
        Debug.WriteLine($"[SearXNG] All {_instancePool.Count} instances exhausted (tried {attemptCount}).");

        // Return empty — caller will fall through to other providers.
        return new List<SearchResult>();
    }

    /// <summary>
    /// Probes a batch of instances in parallel. Returns the first successful response,
    /// or null if none succeeded.
    /// </summary>
    private async Task<List<SearchResult>> TryProbeBatchAsync(
        List<string> instances, string query, int maxResults, CancellationToken ct)
    {
        // Per-batch timeout: don't let a batch of 3 outlive their allowed window.
        // We do NOT cancel on success — remaining tasks complete in the background
        // (their dead-marking is based on actual health, not batch cancellation).
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        batchCts.CancelAfter(TimeSpan.FromSeconds(_perInstanceTimeout.TotalSeconds * 1.5));

        var tasks = instances.Select(url => WrapInstanceProbe(url, query, maxResults, batchCts.Token)).ToList();

        while (tasks.Count > 0)
        {
            Task<List<SearchResult>> completed;
            try
            {
                completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            tasks.Remove(completed);

            try
            {
                var results = await completed.ConfigureAwait(false);
                if (results.Count > 0)
                {
                    // First successful response wins. Remaining tasks run in background.
                    return results;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // This instance timed out (per-instance timeout) — continue waiting.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearXNG] Instance probe failed: {ex.Message}");
            }
        }

        return null; // No instance in this batch succeeded.
    }

    /// <summary>
    /// Wraps TrySearchInstanceAsync with dead-instance tracking.
    /// On failure (real error, not user cancellation) the instance is marked dead
    /// for DeadInstanceRetryAfter so we skip it on subsequent queries.
    /// </summary>
    private async Task<List<SearchResult>> WrapInstanceProbe(
        string baseUrl, string query, int maxResults, CancellationToken ct)
    {
        try
        {
            var results = await TrySearchInstanceAsync(baseUrl, query, maxResults, ct).ConfigureAwait(false);

            // Success — clear any previous death mark.
            _deadInstances.TryRemove(baseUrl, out _);
            return results;
        }
        catch (OperationCanceledException)
        {
            // User cancellation — don't mark as dead; just rethrow.
            throw;
        }
        catch (Exception ex)
        {
            // Real failure — mark this instance as dead for a while.
            var until = DateTime.UtcNow + DeadInstanceRetryAfter;
            _deadInstances[baseUrl] = until;
            Debug.WriteLine($"[SearXNG] Marked {baseUrl} dead until {until:HH:mm:ss} ({ex.GetType().Name}: {ex.Message})");
            throw;
        }
    }

    private async Task<List<SearchResult>> TrySearchInstanceAsync(
        string baseUrl, string query, int maxResults, CancellationToken ct)
    {
        var url = baseUrl + "/search?q=" + Uri.EscapeDataString(query) + "&format=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        // Per-instance timeout.
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
