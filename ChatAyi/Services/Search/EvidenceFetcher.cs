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

    public async Task<EvidenceFetchResult> FetchAsync(
        IReadOnlyList<SearchCandidate> candidates,
        int maxAttempts,
        int targetPages,
        int maxPagesPerDomain,
        CancellationToken ct)
    {
        var ordered = (candidates ?? Array.Empty<SearchCandidate>()).ToList();

        var pages = new List<EvidencePage>();
        var perDomain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
            if (current >= maxPagesPerDomain)
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

        return new EvidenceFetchResult(pages, attempts);
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
