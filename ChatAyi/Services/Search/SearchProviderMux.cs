using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChatAyi.Services;

namespace ChatAyi.Services.Search;

public sealed class SearchProviderMux
{
    private readonly SearxngSearchClient _searxng;
    private readonly HttpClient _http;
    private readonly DdgSearchClient _ddgFallback;

    public SearchProviderMux(SearxngSearchClient searxng, HttpClient http, DdgSearchClient ddgFallback = null)
    {
        _searxng = searxng;
        _http = http;
        _ddgFallback = ddgFallback;
    }

    public async Task<IReadOnlyList<SearchCandidate>> SearchCandidatesAsync(
        string query,
        int maxCandidates,
        SearchIntent intent,
        CancellationToken ct)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0)
            return Array.Empty<SearchCandidate>();

        maxCandidates = Math.Clamp(Math.Max(maxCandidates, 8), 8, 12);
        var combined = new List<SearchCandidate>();

        // 1) SearXNG primary
        try
        {
            var searx = await SearchSearxAsync(query, maxCandidates, intent, ct);
            MergeCandidates(combined, searx, maxCandidates, intent);
            Debug.WriteLine($"[SearchMux] after-searx count={combined.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SearchMux] searx error={ex.Message}");
        }

        // 2) Jina booster only
        if (NeedsMoreCandidates(combined, intent))
        {
            try
            {
                var jina = await SearchJinaAsync(query, maxCandidates, intent, ct);
                MergeCandidates(combined, jina, maxCandidates, intent);
                Debug.WriteLine($"[SearchMux] after-jina count={combined.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchMux] jina error={ex.Message}");
            }
        }

        // 3) GitHub only for code/docs intent
        if (ShouldUseGitHub(intent) && NeedsMoreCandidates(combined, intent))
        {
            try
            {
                var github = await SearchGitHubAsync(query, maxCandidates, intent, ct);
                MergeCandidates(combined, github, maxCandidates, intent);
                Debug.WriteLine($"[SearchMux] after-github count={combined.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchMux] github error={ex.Message}");
            }
        }

        // 4) Wikipedia last resort only
        if (NeedsMoreCandidates(combined, intent))
        {
            try
            {
                var wiki = await SearchWikipediaAsync(query, 1, intent, ct);
                MergeCandidates(combined, wiki, maxCandidates, intent);
                Debug.WriteLine($"[SearchMux] after-wikipedia count={combined.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchMux] wikipedia error={ex.Message}");
            }
        }

        // 5) DDG emergency only
        if (combined.Count == 0 && _ddgFallback is not null)
        {
            try
            {
                var ddg = await SearchDdgAsync(query, maxCandidates, intent, ct);
                MergeCandidates(combined, ddg, maxCandidates, intent);
                Debug.WriteLine($"[SearchMux] after-ddg count={combined.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SearchMux] ddg error={ex.Message}");
            }
        }

        return combined.Take(maxCandidates).ToList();
    }

    private async Task<List<SearchCandidate>> SearchSearxAsync(string query, int maxResults, SearchIntent intent, CancellationToken ct)
    {
        var rows = await _searxng.SearchAsync(query, maxResults, ct);
        return rows
            .Select(r => CreateCandidate(r.Title, r.Url, r.Snippet, "searxng", intent))
            .Where(x => x is not null)
            .Cast<SearchCandidate>()
            .ToList();
    }

    private async Task<List<SearchCandidate>> SearchJinaAsync(string query, int maxResults, SearchIntent intent, CancellationToken ct)
    {
        var url = "https://s.jina.ai/?q=" + Uri.EscapeDataString(query);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "text/plain, text/markdown;q=0.9, */*;q=0.8");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(raw))
            return new List<SearchCandidate>();

        var outList = new List<SearchCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var linkMatches = Regex.Matches(
            raw,
            "\\[(?<title>[^\\]]{2,200})\\]\\((?<url>https?://[^)\\s]+)\\)",
            RegexOptions.IgnoreCase);

        foreach (Match match in linkMatches)
        {
            var title = (match.Groups["title"].Value ?? string.Empty).Trim();
            var link = (match.Groups["url"].Value ?? string.Empty).Trim();
            if (link.Length == 0 || !seen.Add(SearchUrlHelpers.NormalizeUrlKey(link)))
                continue;

            var snippet = ExtractLocalSnippet(raw, match.Index, match.Length, title);
            var candidate = CreateCandidate(title, link, snippet, "jina", intent);
            if (candidate is not null)
                outList.Add(candidate);

            if (outList.Count >= maxResults)
                break;
        }

        if (outList.Count < Math.Min(4, maxResults))
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
                if (link.Length == 0 || !seen.Add(SearchUrlHelpers.NormalizeUrlKey(link)))
                    continue;

                var title = Regex.Replace(trimmed.Replace(link, " "), "^[-*\\d\\.)\\s]+", string.Empty).Trim();
                var snippet = title.Length > 0 ? title : trimmed;
                var candidate = CreateCandidate(title, link, snippet, "jina", intent);
                if (candidate is not null)
                    outList.Add(candidate);

                if (outList.Count >= maxResults)
                    break;
            }
        }

        return outList;
    }

    private async Task<List<SearchCandidate>> SearchGitHubAsync(string query, int maxResults, SearchIntent intent, CancellationToken ct)
    {
        var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query)}&per_page={maxResults}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(text))
            return new List<SearchCandidate>();

        var outList = new List<SearchCandidate>();
        using var doc = JsonDocument.Parse(text);

        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return outList;

        foreach (var item in items.EnumerateArray())
        {
            var title = item.TryGetProperty("full_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : string.Empty;
            var link = item.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : string.Empty;
            var snippet = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : string.Empty;

            var candidate = CreateCandidate(title ?? string.Empty, link ?? string.Empty, snippet ?? string.Empty, "github", intent);
            if (candidate is not null)
                outList.Add(candidate);

            if (outList.Count >= maxResults)
                break;
        }

        return outList;
    }

    private async Task<List<SearchCandidate>> SearchWikipediaAsync(string query, int maxResults, SearchIntent intent, CancellationToken ct)
    {
        var url = $"https://en.wikipedia.org/w/api.php?action=opensearch&search={Uri.EscapeDataString(query)}&limit={maxResults}&namespace=0&format=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(text))
            return new List<SearchCandidate>();

        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 4)
            return new List<SearchCandidate>();

        var titles = doc.RootElement[1];
        var descs = doc.RootElement[2];
        var urls = doc.RootElement[3];

        var outList = new List<SearchCandidate>();
        for (var i = 0; i < Math.Min(maxResults, urls.GetArrayLength()); i++)
        {
            var title = titles.GetArrayLength() > i && titles[i].ValueKind == JsonValueKind.String ? titles[i].GetString() : string.Empty;
            var link = urls[i].ValueKind == JsonValueKind.String ? urls[i].GetString() : string.Empty;
            var snippet = descs.ValueKind == JsonValueKind.Array && descs.GetArrayLength() > i && descs[i].ValueKind == JsonValueKind.String ? descs[i].GetString() : string.Empty;

            var candidate = CreateCandidate(title ?? string.Empty, link ?? string.Empty, snippet ?? string.Empty, "wikipedia", intent);
            if (candidate is not null)
                outList.Add(candidate);
        }

        return outList;
    }

    private async Task<List<SearchCandidate>> SearchDdgAsync(string query, int maxResults, SearchIntent intent, CancellationToken ct)
    {
        if (_ddgFallback is null)
            return new List<SearchCandidate>();

        var rows = await _ddgFallback.SearchAsync(query, maxResults, ct);
        return rows
            .Select(r => CreateCandidate(r.Title, r.Url, r.Snippet, "ddg", intent))
            .Where(x => x is not null)
            .Cast<SearchCandidate>()
            .ToList();
    }

    private static void MergeCandidates(List<SearchCandidate> target, IEnumerable<SearchCandidate> incoming, int maxCandidates, SearchIntent intent)
    {
        var incomingFiltered = incoming
            .Where(x => x is not null)
            .Select(x => CreateCandidate(x.Title, x.Url, x.Snippet, x.Source, intent))
            .Where(x => x is not null)
            .Cast<SearchCandidate>()
            .OrderByDescending(x => x.Score)
            .ToList();

        var seenUrls = new HashSet<string>(
            target.Select(x => SearchUrlHelpers.NormalizeUrlKey(x.Url)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in incomingFiltered)
        {
            var key = SearchUrlHelpers.NormalizeUrlKey(item.Url);
            if (!seenUrls.Add(key))
                continue;

            target.Add(item);
        }

        target.Sort((a, b) => b.Score.CompareTo(a.Score));
        PruneCandidatePool(target, maxCandidates);
    }

    private static void PruneCandidatePool(List<SearchCandidate> items, int maxCandidates)
    {
        var kept = new List<SearchCandidate>();
        var perDomain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var wikiCount = 0;

        foreach (var item in items.OrderByDescending(x => x.Score))
        {
            var domain = SearchUrlHelpers.TryGetDomain(item.Url);
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            if (SearchUrlHelpers.IsWikipediaUrl(item.Url))
            {
                if (wikiCount >= 1)
                    continue;

                wikiCount++;
            }

            if (perDomain.TryGetValue(domain, out var current) && current >= 2)
                continue;

            perDomain[domain] = current + 1;
            kept.Add(item);

            if (kept.Count >= maxCandidates)
                break;
        }

        items.Clear();
        items.AddRange(kept);
    }

    private static bool NeedsMoreCandidates(IReadOnlyList<SearchCandidate> items, SearchIntent intent)
    {
        const int minCount = 6;
        const int minDomains = 4;
        var minNonWiki = intent is SearchIntent.PersonEntity or SearchIntent.General ? 3 : 2;

        if (items.Count < minCount)
            return true;

        var domains = items
            .Select(x => SearchUrlHelpers.TryGetDomain(x.Url))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (domains < minDomains)
            return true;

        var nonWiki = items.Count(x => !SearchUrlHelpers.IsWikipediaUrl(x.Url));
        return nonWiki < minNonWiki;
    }

    private static bool ShouldUseGitHub(SearchIntent intent)
        => intent is SearchIntent.CodeRepo or SearchIntent.Documentation;

    private static SearchCandidate CreateCandidate(string title, string url, string snippet, string source, SearchIntent intent)
    {
        title = (title ?? string.Empty).Trim();
        url = (url ?? string.Empty).Trim();
        snippet = NormalizeSnippet(title, snippet);
        source = (source ?? string.Empty).Trim();

        if (url.Length == 0)
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme is not ("http" or "https"))
            return null;

        if (IsLowQualitySource(uri, title, snippet, source))
            return null;

        if (title.Length == 0)
            title = url;

        var score = GetCandidatePriority(uri, title, snippet, source, intent);
        return new SearchCandidate(title, url, snippet, source, score);
    }

    private static string NormalizeSnippet(string title, string snippet)
    {
        snippet = (snippet ?? string.Empty).Trim();
        if (snippet.Length >= 40)
            return snippet;

        title = (title ?? string.Empty).Trim();
        return title;
    }

    private static string ExtractLocalSnippet(string raw, int index, int length, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        var start = Math.Max(0, index - 160);
        var end = Math.Min(raw.Length, index + length + 160);
        var slice = raw.Substring(start, end - start);
        slice = Regex.Replace(slice, "\\s+", " ").Trim();
        return slice.Length > 220 ? slice.Substring(0, 220) : slice;
    }

    private static double GetCandidatePriority(Uri uri, string title, string snippet, string source, SearchIntent intent)
    {
        var score = 0d;
        var host = SearchUrlHelpers.NormalizeDomainKey(uri.Host);
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
        var text = ((title ?? string.Empty) + " " + (snippet ?? string.Empty) + " " + (source ?? string.Empty)).ToLowerInvariant();

        if (host.Contains("docs.", StringComparison.Ordinal)
            || host.Contains("developer.", StringComparison.Ordinal)
            || host.Contains("readthedocs", StringComparison.Ordinal)
            || path.Contains("/docs", StringComparison.Ordinal)
            || path.Contains("/reference", StringComparison.Ordinal)
            || path.Contains("/guide", StringComparison.Ordinal)
            || path.Contains("/api", StringComparison.Ordinal)
            || text.Contains("official", StringComparison.Ordinal)
            || text.Contains("documentation", StringComparison.Ordinal))
        {
            score += 4;
        }

        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            if (intent is SearchIntent.CodeRepo or SearchIntent.Documentation)
                score += 3;
            else
                score -= 3;
        }

        if (SearchUrlHelpers.IsWikipediaUrl(uri.AbsoluteUri))
        {
            score += intent == SearchIntent.PersonEntity ? -3 : -2;
        }

        if (snippet.Length >= 80)
            score += 0.5;

        return score;
    }

    private static bool IsLowQualitySource(Uri uri, string title, string snippet, string source)
    {
        var host = SearchUrlHelpers.NormalizeDomainKey(uri.Host);
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();

        if (host is "s.jina.ai" or "r.jina.ai")
            return true;

        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && IsLikelyLowValueGithubUrl(path))
            return true;

        if (HasLowValuePathToken(path) && !LooksLikeReferencePage(uri, title, snippet, source))
            return true;

        return false;
    }

    private static bool IsLikelyLowValueGithubUrl(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return true;

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
        var host = SearchUrlHelpers.NormalizeDomainKey(uri.Host);
        var path = (uri.AbsolutePath ?? string.Empty).ToLowerInvariant();
        var text = ((title ?? string.Empty) + " " + (snippet ?? string.Empty) + " " + (source ?? string.Empty)).ToLowerInvariant();

        if (host.Contains("docs.", StringComparison.Ordinal)
            || host.Contains("developer.", StringComparison.Ordinal)
            || host.Contains("readthedocs", StringComparison.Ordinal)
            || host.Contains("wikipedia.org", StringComparison.Ordinal))
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
}
