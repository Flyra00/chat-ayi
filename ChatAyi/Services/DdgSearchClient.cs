using System.Net.Http;
using System.Text.Json;

namespace ChatAyi.Services;

public sealed class DdgSearchClient
{
    private readonly HttpClient _http;

    private static readonly string[] Endpoints =
    {
        "https://api.duckduckgo.com/",
        "https://duckduckgo.com/"
    };

    public DdgSearchClient(HttpClient http)
    {
        _http = http;
    }

    public sealed record SearchResult(string Title, string Url, string Snippet);

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return new List<SearchResult>();

        maxResults = Math.Clamp(maxResults, 1, 10);

        var qs = $"?q={Uri.EscapeDataString(query)}&format=json&no_html=1&no_redirect=1&skip_disambig=1";
        string text = string.Empty;

        Exception last = null;
        foreach (var baseUrl in Endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + qs);
                req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
                req.Headers.TryAddWithoutValidation("Accept", "application/json,text/javascript,*/*;q=0.8");

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                text = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException(string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)resp.StatusCode}" : text);

                // success
                last = null;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                continue;
            }
        }

        if (last is not null)
        {
            var inner = last.InnerException?.Message;
            throw new HttpRequestException(
                "DuckDuckGo search failed: " + (inner ?? last.Message),
                last);
        }

        var results = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var heading = root.TryGetProperty("Heading", out var h) && h.ValueKind == JsonValueKind.String
                ? (h.GetString() ?? string.Empty).Trim()
                : string.Empty;

            var abstractText = root.TryGetProperty("AbstractText", out var at) && at.ValueKind == JsonValueKind.String
                ? (at.GetString() ?? string.Empty).Trim()
                : string.Empty;

            var abstractUrl = root.TryGetProperty("AbstractURL", out var au) && au.ValueKind == JsonValueKind.String
                ? (au.GetString() ?? string.Empty).Trim()
                : string.Empty;

            if (abstractUrl.Length > 0)
            {
                AddResult(results, seen,
                    new SearchResult(
                        heading.Length > 0 ? heading : query,
                        abstractUrl,
                        abstractText));
            }

            if (root.TryGetProperty("Results", out var res) && res.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in res.EnumerateArray())
                {
                    if (TryParseTopic(item, out var r))
                        AddResult(results, seen, r);
                    if (results.Count >= maxResults) break;
                }
            }

            if (results.Count < maxResults && root.TryGetProperty("RelatedTopics", out var rel) && rel.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rel.EnumerateArray())
                {
                    ExtractRelated(item, results, seen, maxResults);
                    if (results.Count >= maxResults) break;
                }
            }
        }
        catch
        {
            return new List<SearchResult>();
        }

        return results.Take(maxResults).ToList();
    }

    private static void ExtractRelated(JsonElement element, List<SearchResult> results, HashSet<string> seen, int maxResults)
    {
        if (results.Count >= maxResults) return;

        if (element.ValueKind != JsonValueKind.Object) return;

        if (TryParseTopic(element, out var r))
        {
            AddResult(results, seen, r);
            return;
        }

        if (element.TryGetProperty("Topics", out var topics) && topics.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in topics.EnumerateArray())
            {
                ExtractRelated(t, results, seen, maxResults);
                if (results.Count >= maxResults) break;
            }
        }
    }

    private static bool TryParseTopic(JsonElement obj, out SearchResult result)
    {
        result = null;
        if (obj.ValueKind != JsonValueKind.Object) return false;

        var url = obj.TryGetProperty("FirstURL", out var u) && u.ValueKind == JsonValueKind.String
            ? (u.GetString() ?? string.Empty).Trim()
            : string.Empty;
        if (url.Length == 0) return false;

        var title = obj.TryGetProperty("Text", out var t) && t.ValueKind == JsonValueKind.String
            ? (t.GetString() ?? string.Empty).Trim()
            : string.Empty;

        // Often this duplicates Text; keep it as snippet.
        var snippet = title;
        result = new SearchResult(title.Length > 0 ? title : url, url, snippet);
        return true;
    }

    private static void AddResult(List<SearchResult> results, HashSet<string> seen, SearchResult r)
    {
        if (r is null) return;
        if (string.IsNullOrWhiteSpace(r.Url)) return;

        if (!Uri.TryCreate(r.Url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not ("http" or "https")) return;
        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase)) return;

        var key = uri.ToString();
        if (!seen.Add(key)) return;
        results.Add(r);
    }
}
