namespace ChatAyi.Services.Search;

public sealed class SearchOrchestrator
{
    private readonly SearchIntentClassifier _intentClassifier;
    private readonly SearchProviderMux _providerMux;
    private readonly EvidenceFetcher _evidenceFetcher;
    private readonly PassageExtractor _passageExtractor;
    private readonly SearchHealthEvaluator _healthEvaluator;

    public SearchOrchestrator(
        SearchIntentClassifier intentClassifier,
        SearchProviderMux providerMux,
        EvidenceFetcher evidenceFetcher,
        PassageExtractor passageExtractor,
        SearchHealthEvaluator healthEvaluator)
    {
        _intentClassifier = intentClassifier;
        _providerMux = providerMux;
        _evidenceFetcher = evidenceFetcher;
        _passageExtractor = passageExtractor;
        _healthEvaluator = healthEvaluator;
    }

    public async Task<SearchGroundingBundle> RunAsync(string query, CancellationToken ct)
    {
        var intent = _intentClassifier.Classify(query);
        var candidates = (await _providerMux.SearchCandidatesAsync(query, 12, intent, ct)).ToList();

        var notes = new List<string>();

        var nonWikiCandidates = candidates
            .Where(c => !SearchUrlHelpers.IsWikipediaUrl(c.Url))
            .ToList();

        var wikiCandidates = candidates
            .Where(c => SearchUrlHelpers.IsWikipediaUrl(c.Url))
            .ToList();

        // Stage 1: non-wiki first
        var stage1 = await _evidenceFetcher.FetchAsync(
            nonWikiCandidates,
            maxAttempts: 12,
            targetPages: 6,
            maxPagesPerDomain: 2,
            ct);

        var pages = stage1.Pages.ToList();
        var passages = _passageExtractor.Extract(query, pages, maxPerPage: 2, maxTotal: 10).ToList();
        var health = _healthEvaluator.Evaluate(intent, pages, passages);
        notes.Add($"stage1={health.Status}");

        var totalAttempts = stage1.Attempts;

        // Stage 2: remaining non-wiki first, then wiki supplement only if still unhealthy
        if (!health.IsHealthy)
        {
            var remaining = BuildRemainingCandidates(nonWikiCandidates, wikiCandidates, pages);

            var stage2 = await _evidenceFetcher.FetchAsync(
                remaining,
                maxAttempts: 8,
                targetPages: 8,
                maxPagesPerDomain: 2,
                ct);

            totalAttempts += stage2.Attempts;

            foreach (var page in stage2.Pages)
            {
                if (pages.All(p => !SearchUrlHelpers.IsSameUrl(p.Url, page.Url)))
                    pages.Add(page);
            }

            passages = _passageExtractor.Extract(query, pages, maxPerPage: 2, maxTotal: 10).ToList();
            health = _healthEvaluator.Evaluate(intent, pages, passages);
            notes.Add($"stage2={health.Status}");
        }

        var diagnostics = new SearchDiagnostics(
            CandidateCount: candidates.Count,
            NonWikiCandidateCount: candidates.Count(c => !SearchUrlHelpers.IsWikipediaUrl(c.Url)),
            PageCount: pages.Count,
            NonWikiPageCount: pages.Count(p => !SearchUrlHelpers.IsWikipediaUrl(p.Url)),
            PassageCount: passages.Count,
            NonWikiPassageCount: passages.Count(p => !SearchUrlHelpers.IsWikipediaUrl(p.Url)),
            FetchAttempts: totalAttempts,
            Notes: notes);

        return new SearchGroundingBundle(
            intent,
            candidates,
            pages,
            passages,
            health,
            diagnostics);
    }

    internal static IReadOnlyList<SearchCandidate> BuildRemainingCandidates(
        IReadOnlyList<SearchCandidate> nonWikiCandidates,
        IReadOnlyList<SearchCandidate> wikiCandidates,
        IReadOnlyList<EvidencePage> pages)
    {
        var remaining = new List<SearchCandidate>();

        remaining.AddRange((nonWikiCandidates ?? Array.Empty<SearchCandidate>()).Where(c =>
            pages.All(p => !SearchUrlHelpers.IsSameUrl(p.Url, c.Url))));

        remaining.AddRange((wikiCandidates ?? Array.Empty<SearchCandidate>()).Where(c =>
            pages.All(p => !SearchUrlHelpers.IsSameUrl(p.Url, c.Url))));

        return remaining;
    }
}
