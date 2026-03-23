namespace ChatAyi.Services.Search;

public sealed class SearchOrchestrator
{
    private readonly SearchIntentClassifier _intentClassifier;
    private readonly SearchProviderMux _providerMux;
    private readonly EvidenceFetcher _evidenceFetcher;
    private readonly PassageExtractor _passageExtractor;

    public SearchOrchestrator(
        SearchIntentClassifier intentClassifier,
        SearchProviderMux providerMux,
        EvidenceFetcher evidenceFetcher,
        PassageExtractor passageExtractor)
    {
        _intentClassifier = intentClassifier;
        _providerMux = providerMux;
        _evidenceFetcher = evidenceFetcher;
        _passageExtractor = passageExtractor;
    }

    public async Task<SearchGroundingBundle> RunAsync(string query, CancellationToken ct)
    {
        var intent = _intentClassifier.Classify(query);
        var candidates = await _providerMux.SearchCandidatesAsync(query, 10, intent, ct);
        var pages = await _evidenceFetcher.FetchAsync(query, candidates, ct);
        var passages = _passageExtractor.Extract(query, pages, maxPerPage: 2, maxTotal: 8);

        return new SearchGroundingBundle(intent, candidates, pages, passages);
    }
}
