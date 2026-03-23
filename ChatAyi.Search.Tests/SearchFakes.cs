using ChatAyi.Services.Search;

namespace ChatAyi.Services.Search;

public sealed class SearchProviderMux
{
    public Func<string, int, SearchIntent, CancellationToken, Task<IReadOnlyList<SearchCandidate>>> SearchCandidatesHandler { get; set; }
        = (_, _, _, _) => Task.FromResult<IReadOnlyList<SearchCandidate>>(Array.Empty<SearchCandidate>());

    public Task<IReadOnlyList<SearchCandidate>> SearchCandidatesAsync(string query, int maxCandidates, SearchIntent intent, CancellationToken ct)
        => SearchCandidatesHandler(query, maxCandidates, intent, ct);
}

public sealed class EvidenceFetcher
{
    public int CallCount { get; private set; }
    public List<IReadOnlyList<SearchCandidate>> CandidateCalls { get; } = new();

    public Func<IReadOnlyList<SearchCandidate>, int, int, int, CancellationToken, Task<EvidenceFetchResult>> FetchHandler { get; set; }
        = (_, _, _, _, _) => Task.FromResult(new EvidenceFetchResult(Array.Empty<EvidencePage>(), 0));

    public Task<EvidenceFetchResult> FetchAsync(
        IReadOnlyList<SearchCandidate> candidates,
        int maxAttempts,
        int targetPages,
        int maxPagesPerDomain,
        CancellationToken ct)
    {
        CallCount++;
        CandidateCalls.Add(candidates?.ToList() ?? new List<SearchCandidate>());
        return FetchHandler(candidates, maxAttempts, targetPages, maxPagesPerDomain, ct);
    }
}

public sealed class PassageExtractor
{
    public Func<string, IReadOnlyList<EvidencePage>, int, int, IReadOnlyList<EvidencePassage>> ExtractHandler { get; set; }
        = (_, _, _, _) => Array.Empty<EvidencePassage>();

    public IReadOnlyList<EvidencePassage> Extract(string query, IReadOnlyList<EvidencePage> pages, int maxPerPage = 2, int maxTotal = 10)
        => ExtractHandler(query, pages, maxPerPage, maxTotal);
}
