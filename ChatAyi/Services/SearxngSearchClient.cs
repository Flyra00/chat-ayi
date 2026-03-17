using System.Net.Http;
using System.Text.Json;

namespace ChatAyi.Services;

public sealed class SearxngSearchClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _baseHost;

    public SearxngSearchClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _baseHost = Uri.TryCreate(_baseUrl, UriKind.Absolute, out var uri)
            ? uri.Host
            : string.Empty;
    }

    public sealed record SearchResult(string Title, string Url, string Snippet);

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return new List<SearchResult>();

        maxResults = Math.Clamp(maxResults, 1, 10);

        var url = _baseUrl + "/search?q=" + Uri.EscapeDataString(query) + "&format=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(12));

        HttpResponseMessage resp;
        string text;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            text = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("SearXNG request timed out");
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)resp.StatusCode}" : text);
        }

        var results = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(text);
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
            if (!string.IsNullOrWhiteSpace(_baseHost) && string.Equals(uri.Host, _baseHost, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(uri.ToString())) continue;

            results.Add(new SearchResult(title.Length > 0 ? title : link, link, snippet));
            if (results.Count >= maxResults) break;
        }

        return results;
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var value = (baseUrl ?? string.Empty).Trim();
        if (value.Length == 0)
            value = "https://searx.be";

        return value.TrimEnd('/');
    }
}
