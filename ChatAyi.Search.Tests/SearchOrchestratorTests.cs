using ChatAyi.Services.Search;

namespace ChatAyi.Search.Tests;

public sealed class SearchOrchestratorTests
{
    [Fact]
    public async Task RunAsync_Stage1UsesNonWikiFirst_ThenStage2WhenUnhealthy()
    {
        var classifier = new SearchIntentClassifier();
        var mux = new SearchProviderMux
        {
            SearchCandidatesHandler = (_, _, _, _) => Task.FromResult<IReadOnlyList<SearchCandidate>>(
                new List<SearchCandidate>
                {
                    new("A", "https://example.com/a", "sa", "searxng", 4),
                    new("B", "https://news.example.net/b", "sb", "jina", 3),
                    new("Wiki", "https://en.wikipedia.org/wiki/A", "sw", "wikipedia", 1)
                })
        };

        var fetcher = new EvidenceFetcher();
        fetcher.FetchHandler = (candidates, _, _, _, _) =>
        {
            if (fetcher.CallCount == 1)
            {
                return Task.FromResult(new EvidenceFetchResult(
                    new List<EvidencePage>
                    {
                        new("https://example.com/a", "A", "text" + new string('x', 400), "searxng")
                    },
                    3));
            }

            return Task.FromResult(new EvidenceFetchResult(
                new List<EvidencePage>
                {
                    new("https://news.example.net/b", "B", "text" + new string('y', 400), "jina"),
                    new("https://en.wikipedia.org/wiki/A", "Wiki", "text" + new string('z', 400), "wikipedia")
                },
                2));
        };

        var extractor = new PassageExtractor
        {
            ExtractHandler = (_, pages, _, _) =>
            {
                var outList = new List<EvidencePassage>();
                var idx = 1;
                foreach (var p in pages)
                {
                    outList.Add(new EvidencePassage(idx++, p.Url, p.Title, p.Source, "passage", 2));
                    outList.Add(new EvidencePassage(idx++, p.Url, p.Title, p.Source, "passage2", 1));
                }

                return outList;
            }
        };

        var orchestrator = new SearchOrchestrator(
            classifier,
            mux,
            fetcher,
            extractor,
            new SearchHealthEvaluator());

        var bundle = await orchestrator.RunAsync("windah basudara", CancellationToken.None);

        Assert.Equal(2, fetcher.CallCount);
        Assert.All(fetcher.CandidateCalls[0], c => Assert.False(SearchUrlHelpers.IsWikipediaUrl(c.Url)));
        Assert.Contains(bundle.Diagnostics.Notes, n => n.StartsWith("stage1="));
        Assert.Contains(bundle.Diagnostics.Notes, n => n.StartsWith("stage2="));
    }

    [Fact]
    public void BuildRemainingCandidates_PrioritizesNonWikiThenWiki()
    {
        var nonWiki = new List<SearchCandidate>
        {
            new("A", "https://example.com/a", "", "searxng", 1),
            new("B", "https://news.example.net/b", "", "jina", 1)
        };

        var wiki = new List<SearchCandidate>
        {
            new("W", "https://en.wikipedia.org/wiki/A", "", "wikipedia", 1)
        };

        var usedPages = new List<EvidencePage>
        {
            new("https://example.com/a", "A", "text", "searxng")
        };

        var remaining = SearchOrchestrator.BuildRemainingCandidates(nonWiki, wiki, usedPages);

        Assert.Equal(2, remaining.Count);
        Assert.Equal("https://news.example.net/b", remaining[0].Url);
        Assert.Equal("https://en.wikipedia.org/wiki/A", remaining[1].Url);
    }
}
