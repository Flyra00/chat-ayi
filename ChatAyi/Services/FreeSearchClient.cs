using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatAyi.Services;

public sealed class FreeSearchClient
{
    private const int StableMaxResults = 5;
    private readonly SearxngSearchClient _searxng;
    private readonly DdgSearchClient _ddgFallback;
    private readonly HttpClient _http;

    public FreeSearchClient(SearxngSearchClient searxng, HttpClient http, DdgSearchClient ddgFallback = null)
    {
        _searxng = searxng;
        _http = http;
        _ddgFallback = ddgFallback;
    }

    public sealed record SearchResult(string Title, string Url, string Snippet, string Source);

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return new List<SearchResult>();
        maxResults = Math.Clamp(maxResults, 1, StableMaxResults);

        var combined = new List<SearchResult>();

        // 1) Jina Search primary
        try
        {
            var jina = await TrySearchWithJinaAsync(query, maxResults, ct);
            jina = FilterResults(jina, maxResults);
            AppendMissingDiverse(combined, jina, maxResults);
        }
        catch
        {
            // ignore
        }

        // 2) SearXNG fallback/fill
        if (combined.Count < 3 || HasLowDomainVariety(combined, minDistinctDomains: 3))
        {
            try
            {
                var searxng = await _searxng.SearchAsync(query, maxResults, ct);
                var mapped = FilterResults(
                    searxng.Select(r => new SearchResult(r.Title, r.Url, r.Snippet, "searxng")),
                    maxResults);
                AppendMissingDiverse(combined, mapped, maxResults);
            }
            catch
            {
                // ignore
            }
        }

        var needsQualityBoost = HasLowDomainVariety(combined, minDistinctDomains: 3)
                                || CountNonWikipedia(combined) < Math.Min(2, maxResults);

        // 3) DuckDuckGo fallback/fill (existing provider)
        if (_ddgFallback is not null && (combined.Count < maxResults || needsQualityBoost))
        {
            try
            {
                var ddg = await _ddgFallback.SearchAsync(query, maxResults, ct);
                var mapped = FilterResults(
                    ddg.Select(r => new SearchResult(r.Title, r.Url, r.Snippet, "ddg")),
                    maxResults);
                AppendMissingDiverse(combined, mapped, maxResults);
            }
            catch
            {
                // ignore
            }
        }

        // 4) GitHub repositories search (unauth, rate-limited)
        if (combined.Count < maxResults || needsQualityBoost)
        {
            try
            {
                var gh = await SearchGitHubAsync(query, Math.Min(5, maxResults), ct);
                gh = FilterResults(gh, maxResults);
                AppendMissingDiverse(combined, gh, maxResults);
            }
            catch
            {
                // ignore
            }
        }

        // 5) Wikipedia OpenSearch
        if (combined.Count < maxResults)
        {
            try
            {
                var wiki = await SearchWikipediaAsync(query, Math.Min(5, maxResults), ct);
                wiki = FilterResults(wiki, maxResults);
                AppendMissingDiverse(combined, wiki, maxResults);
            }
            catch
            {
                // ignore
            }
        }

        if (combined.Count > 0)
            return combined.Take(maxResults).ToList();

        return new List<SearchResult>();
    }

    private async Task<List<SearchResult>> TrySearchWithJinaAsync(string query, int maxResults, CancellationToken ct)
    {
        var url = "https://s.jina.ai/?q=" + Uri.EscapeDataString(query);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "text/plain, text/markdown;q=0.9, */*;q=0.8");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            return new List<SearchResult>();

        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
            return new List<SearchResult>();

        // Expected commonly in markdown-like format: [Title](https://...)
        var outList = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var linkMatches = Regex.Matches(raw, "\\[(?<title>[^\\]]{2,200})\\]\\((?<url>https?://[^)\\s]+)\\)", RegexOptions.IgnoreCase);
        foreach (Match match in linkMatches)
        {
            var title = (match.Groups["title"].Value ?? string.Empty).Trim();
            var link = (match.Groups["url"].Value ?? string.Empty).Trim();
            if (link.Length == 0 || !seen.Add(link))
                continue;

            outList.Add(new SearchResult(title.Length > 0 ? title : link, link, string.Empty, "jina"));
            if (outList.Count >= maxResults)
                break;
        }

        // Fallback parser for plain-text list lines containing URLs.
        if (outList.Count < Math.Min(3, maxResults))
        {
            foreach (var line in raw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 12)
                    continue;

                var m = Regex.Match(trimmed, "(?<url>https?://\\S+)", RegexOptions.IgnoreCase);
                if (!m.Success)
                    continue;

                var link = (m.Groups["url"].Value ?? string.Empty).Trim().TrimEnd(')', ']', ',', '.');
                if (link.Length == 0 || !seen.Add(link))
                    continue;

                var title = Regex.Replace(trimmed.Replace(link, " "), "^[-*\\d\\.)\\s]+", string.Empty).Trim();
                outList.Add(new SearchResult(title.Length > 0 ? title : link, link, string.Empty, "jina"));
                if (outList.Count >= maxResults)
                    break;
            }
        }

        return outList;
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

    private static List<SearchResult> FilterResults(IEnumerable<SearchResult> input, int maxResults)
    {
        var candidates = input
            .Where(x => x is not null)
            .Select(r =>
            {
                var title = (r.Title ?? string.Empty).Trim();
                var url = (r.Url ?? string.Empty).Trim();
                var snippet = (r.Snippet ?? string.Empty).Trim();
                var source = (r.Source ?? string.Empty).Trim();

                if (url.Length == 0)
                    return null;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return null;

                if (uri.Scheme is not ("http" or "https"))
                    return null;

                if (IsLowQualitySource(uri, title, snippet, source))
                    return null;

                var score = GetSourcePriority(uri, title, snippet, source);
                return new { title, url, snippet, source, uri, score };
            })
            .Where(x => x is not null)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.url, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outList = new List<SearchResult>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in candidates)
        {
            var title = c.title;
            var url = c.url;
            var snippet = c.snippet;
            var source = c.source;
            var uri = c.uri;

            var urlKey = NormalizeUrlKey(uri);
            if (!seenUrls.Add(urlKey)) continue;

            var domainKey = NormalizeDomainKey(uri.Host);
            if (domainKey.Length == 0) continue;
            if (!seenDomains.Add(domainKey)) continue;

            if (title.Length == 0) title = url;
            if (title.Length < 3 && snippet.Length < 3) continue;

            outList.Add(new SearchResult(title, url, snippet, source));
            if (outList.Count >= maxResults) break;
        }

        return outList;
    }

    private static void AppendMissingDiverse(List<SearchResult> target, IEnumerable<SearchResult> source, int maxResults)
    {
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var existing in target)
        {
            if (string.IsNullOrWhiteSpace(existing?.Url))
                continue;

            if (!Uri.TryCreate(existing.Url, UriKind.Absolute, out var uri))
                continue;

            seenUrls.Add(NormalizeUrlKey(uri));
            var domain = NormalizeDomainKey(uri.Host);
            if (domain.Length > 0)
                seenDomains.Add(domain);
        }

        foreach (var item in source)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Url))
                continue;

            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                continue;

            var urlKey = NormalizeUrlKey(uri);
            if (!seenUrls.Add(urlKey))
                continue;

            var domain = NormalizeDomainKey(uri.Host);
            if (domain.Length == 0 || !seenDomains.Add(domain))
                continue;

            if (target.Count >= maxResults)
            {
                if (!IsWikipedia(uri))
                {
                    var wikiIdx = FindFirstWikipediaIndex(target);
                    if (wikiIdx >= 0)
                    {
                        target[wikiIdx] = item;
                        RebuildSeenSets(target, seenUrls, seenDomains);
                    }
                }

                continue;
            }

            target.Add(item);
        }
    }

    private static string NormalizeUrlKey(Uri uri)
    {
        var left = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(uri.Query))
            return left;

        return left + uri.Query;
    }

    private static string NormalizeDomainKey(string host)
    {
        var value = (host ?? string.Empty).Trim().ToLowerInvariant();
        if (value.StartsWith("www.", StringComparison.Ordinal))
            value = value.Substring(4);

        if (value.EndsWith(".wikipedia.org", StringComparison.Ordinal))
            value = "wikipedia.org";

        return value;
    }

    private static int GetSourcePriority(Uri uri, string title, string snippet, string source)
    {
        var score = 0;
        var host = NormalizeDomainKey(uri.Host);
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
        var text = ((title ?? string.Empty) + " " + (snippet ?? string.Empty) + " " + (source ?? string.Empty)).ToLowerInvariant();

        if (host.Contains("docs.", StringComparison.Ordinal)
            || host.Contains("developer.", StringComparison.Ordinal)
            || host.Contains("readthedocs", StringComparison.Ordinal)
            || path.Contains("/docs", StringComparison.Ordinal)
            || path.Contains("/reference", StringComparison.Ordinal)
            || path.Contains("/api", StringComparison.Ordinal)
            || text.Contains("official", StringComparison.Ordinal)
            || text.Contains("documentation", StringComparison.Ordinal))
        {
            score += 4;
        }

        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 2)
                score += 3;
        }

        if (IsWikipedia(uri))
            score -= 2;

        return score;
    }

    private static int CountNonWikipedia(IEnumerable<SearchResult> items)
    {
        var count = 0;
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Url))
                continue;

            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                continue;

            if (!IsWikipedia(uri))
                count++;
        }

        return count;
    }

    private static bool IsWikipedia(Uri uri)
        => NormalizeDomainKey(uri.Host).Equals("wikipedia.org", StringComparison.Ordinal);

    private static int FindFirstWikipediaIndex(IReadOnlyList<SearchResult> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item is null || string.IsNullOrWhiteSpace(item.Url))
                continue;

            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                continue;

            if (IsWikipedia(uri))
                return i;
        }

        return -1;
    }

    private static void RebuildSeenSets(IEnumerable<SearchResult> target, HashSet<string> seenUrls, HashSet<string> seenDomains)
    {
        seenUrls.Clear();
        seenDomains.Clear();

        foreach (var existing in target)
        {
            if (existing is null || string.IsNullOrWhiteSpace(existing.Url))
                continue;

            if (!Uri.TryCreate(existing.Url, UriKind.Absolute, out var uri))
                continue;

            seenUrls.Add(NormalizeUrlKey(uri));
            var domain = NormalizeDomainKey(uri.Host);
            if (domain.Length > 0)
                seenDomains.Add(domain);
        }
    }

    private static bool IsLowQualitySource(Uri uri, string title, string snippet, string source)
    {
        var host = NormalizeDomainKey(uri.Host);
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();

        // Jina endpoints are transport/search helpers, not final reference sources.
        if (host is "s.jina.ai" or "r.jina.ai")
            return true;

        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && IsLikelyLowValueGithubUrl(path))
        {
            return true;
        }

        if (HasLowValuePathToken(path) && !LooksLikeReferencePage(uri, title, snippet, source))
            return true;

        return false;
    }

    private static bool IsLikelyLowValueGithubUrl(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return true;

        // owner/repo is allowed.
        if (segments.Length == 2)
            return false;

        var section = segments[2];
        if (section is "wiki" or "releases")
            return false;

        if (section is "blob" or "tree")
        {
            if (segments.Length < 5)
                return true;

            var branch = segments[3];
            var node = segments[4];
            if (branch is "main" or "master")
            {
                if (node.Equals("readme.md", StringComparison.OrdinalIgnoreCase)
                    || node.Equals("docs", StringComparison.OrdinalIgnoreCase)
                    || node.Equals("doc", StringComparison.OrdinalIgnoreCase)
                    || node.Equals("wiki", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        // Other deep GitHub pages are usually low-value grounding sources.
        return true;
    }

    private static bool HasLowValuePathToken(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var lowValueTokens = new[]
        {
            "assignment", "tugas", "praktikum", "homework", "uts", "uas",
            "kuliah", "dump", "tmp", "temp", "sandbox", "demo", "test"
        };

        foreach (var token in lowValueTokens)
        {
            if (path.Contains('/' + token, StringComparison.Ordinal)
                || path.Contains(token + '-', StringComparison.Ordinal)
                || path.Contains('-' + token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeReferencePage(Uri uri, string title, string snippet, string source)
    {
        var host = NormalizeDomainKey(uri.Host);
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
        var text = ((title ?? string.Empty) + " " + (snippet ?? string.Empty) + " " + (source ?? string.Empty)).ToLowerInvariant();

        if (host.Contains("docs.", StringComparison.Ordinal)
            || host.Contains("wikipedia.org", StringComparison.Ordinal)
            || host.Contains("developer.", StringComparison.Ordinal)
            || host.Contains("readthedocs", StringComparison.Ordinal))
        {
            return true;
        }

        if (path.Contains("/docs", StringComparison.Ordinal)
            || path.Contains("/documentation", StringComparison.Ordinal)
            || path.Contains("/wiki", StringComparison.Ordinal)
            || path.Contains("/reference", StringComparison.Ordinal)
            || path.Contains("/guide", StringComparison.Ordinal)
            || path.Contains("/api", StringComparison.Ordinal))
        {
            return true;
        }

        return text.Contains("official", StringComparison.Ordinal)
            || text.Contains("documentation", StringComparison.Ordinal)
            || text.Contains("reference", StringComparison.Ordinal)
            || text.Contains("guide", StringComparison.Ordinal)
            || text.Contains("api", StringComparison.Ordinal);
    }

    private static bool HasLowDomainVariety(IReadOnlyCollection<SearchResult> items, int minDistinctDomains)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Url))
                continue;

            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
                continue;

            var domain = NormalizeDomainKey(uri.Host);
            if (domain.Length == 0)
                continue;

            domains.Add(domain);
            if (domains.Count >= minDistinctDomains)
                return false;
        }

        return true;
    }
}
