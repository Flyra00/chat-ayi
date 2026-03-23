using System.Text.RegularExpressions;
using ChatAyi.Services;

namespace ChatAyi.Services.Search;

public sealed class EvidenceFetcher
{
    private readonly BrowseClient _browse;

    public EvidenceFetcher(BrowseClient browse)
    {
        _browse = browse;
    }

    public async Task<IReadOnlyList<EvidencePage>> FetchAsync(
        string query,
        IReadOnlyList<SearchCandidate> candidates,
        CancellationToken ct)
    {
        var ordered = (candidates ?? Array.Empty<SearchCandidate>())
            .OrderBy(c => SearchUrlHelpers.IsWikipediaUrl(c?.Url) ? 1 : 0)
            .ThenByDescending(c => c?.Score ?? 0)
            .ToList();

        var pages = new List<EvidencePage>();
        var perDomain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        const int maxAttempts = 10;
        const int targetPages = 6;

        var attempts = 0;
        foreach (var candidate in ordered)
        {
            if (attempts >= maxAttempts || pages.Count >= targetPages)
                break;

            if (candidate is null)
                continue;

            attempts++;

            var domain = SearchUrlHelpers.TryGetDomain(candidate.Url);
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            var current = perDomain.TryGetValue(domain, out var value) ? value : 0;
            if (current >= 2)
                continue;

            try
            {
                var page = await _browse.FetchAsync(candidate.Url, ct);
                if (page is null)
                    continue;

                var text = NormalizePageText(page.Text);
                if (text.Length < 300)
                    continue;

                pages.Add(new EvidencePage(
                    page.Url,
                    string.IsNullOrWhiteSpace(page.Title) ? candidate.Title : page.Title,
                    text,
                    candidate.Source));

                perDomain[domain] = current + 1;
            }
            catch
            {
                // keep trying other candidates
            }
        }

        return pages;
    }

    private static string NormalizePageText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }
}
