using System.Net.Http;
using System.Text.Json;

namespace ChatAyi.Services;

public sealed class FreeSearchClient
{
    private readonly DdgSearchClient _ddg;
    private readonly HttpClient _http;

    public FreeSearchClient(DdgSearchClient ddg, HttpClient http)
    {
        _ddg = ddg;
        _http = http;
    }

    public sealed record SearchResult(string Title, string Url, string Snippet, string Source);

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return new List<SearchResult>();
        maxResults = Math.Clamp(maxResults, 1, 10);

        // 1) DuckDuckGo Instant Answer
        try
        {
            var ddg = await _ddg.SearchAsync(query, maxResults, ct);
            if (ddg.Count > 0)
                return ddg.Select(r => new SearchResult(r.Title, r.Url, r.Snippet, "ddg")).ToList();
        }
        catch
        {
            // ignore
        }

        // 2) GitHub repositories search (unauth, rate-limited)
        try
        {
            var gh = await SearchGitHubAsync(query, Math.Min(5, maxResults), ct);
            if (gh.Count > 0)
                return gh;
        }
        catch
        {
            // ignore
        }

        // 3) Wikipedia OpenSearch
        try
        {
            var wiki = await SearchWikipediaAsync(query, Math.Min(5, maxResults), ct);
            if (wiki.Count > 0)
                return wiki;
        }
        catch
        {
            // ignore
        }

        return new List<SearchResult>();
    }

    private async Task<List<SearchResult>> SearchGitHubAsync(string query, int maxResults, CancellationToken ct)
    {
        var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query)}&per_page={maxResults}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return new List<SearchResult>();

        var outList = new List<SearchResult>();
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var item in items.EnumerateArray())
        {
            var name = item.TryGetProperty("full_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : string.Empty;
            var html = item.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : string.Empty;
            var desc = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : string.Empty;

            name = (name ?? string.Empty).Trim();
            html = (html ?? string.Empty).Trim();
            desc = (desc ?? string.Empty).Trim();
            if (html.Length == 0) continue;

            outList.Add(new SearchResult(name.Length > 0 ? name : html, html, desc, "github"));
            if (outList.Count >= maxResults) break;
        }

        return outList;
    }

    private async Task<List<SearchResult>> SearchWikipediaAsync(string query, int maxResults, CancellationToken ct)
    {
        var url = $"https://en.wikipedia.org/w/api.php?action=opensearch&search={Uri.EscapeDataString(query)}&limit={maxResults}&namespace=0&format=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return new List<SearchResult>();

        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<SearchResult>();

        var root = doc.RootElement;
        if (root.GetArrayLength() < 4) return new List<SearchResult>();

        var titles = root[1];
        var descs = root[2];
        var urls = root[3];
        if (titles.ValueKind != JsonValueKind.Array || urls.ValueKind != JsonValueKind.Array) return new List<SearchResult>();

        var outList = new List<SearchResult>();
        for (var i = 0; i < Math.Min(maxResults, urls.GetArrayLength()); i++)
        {
            var title = titles.GetArrayLength() > i && titles[i].ValueKind == JsonValueKind.String ? titles[i].GetString() : string.Empty;
            var link = urls[i].ValueKind == JsonValueKind.String ? urls[i].GetString() : string.Empty;
            var snip = descs.ValueKind == JsonValueKind.Array && descs.GetArrayLength() > i && descs[i].ValueKind == JsonValueKind.String ? descs[i].GetString() : string.Empty;

            title = (title ?? string.Empty).Trim();
            link = (link ?? string.Empty).Trim();
            snip = (snip ?? string.Empty).Trim();
            if (link.Length == 0) continue;

            outList.Add(new SearchResult(title.Length > 0 ? title : link, link, snip, "wikipedia"));
        }

        return outList;
    }
}
