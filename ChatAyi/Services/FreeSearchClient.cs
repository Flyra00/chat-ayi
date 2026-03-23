using ChatAyi.Services.Search;

namespace ChatAyi.Services;

public sealed class FreeSearchClient
{
    private readonly SearchIntentClassifier _intentClassifier;
    private readonly SearchProviderMux _providerMux;

    public FreeSearchClient(
        SearchIntentClassifier intentClassifier,
        SearchProviderMux providerMux)
    {
        _intentClassifier = intentClassifier;
        _providerMux = providerMux;
    }

    public sealed record SearchResult(string Title, string Url, string Snippet, string Source);

    public async Task<List<SearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        maxResults = Math.Clamp(maxResults, 1, 10);

        var intent = _intentClassifier.Classify(query);
        var candidates = await _providerMux.SearchCandidatesAsync(query, Math.Max(6, maxResults), intent, ct);

        return candidates
            .Take(maxResults)
            .Select(c => new SearchResult(c.Title, c.Url, c.Snippet, c.Source))
            .ToList();
    }
}
