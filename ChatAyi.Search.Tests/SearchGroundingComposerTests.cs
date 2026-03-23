using ChatAyi.Services.Search;

namespace ChatAyi.Search.Tests;

public sealed class SearchGroundingComposerTests
{
    [Fact]
    public void BuildBlocks_ReturnsSourcesAndEvidence()
    {
        var composer = new SearchGroundingComposer();
        var bundle = new SearchGroundingBundle(
            SearchIntent.General,
            new List<SearchCandidate>
            {
                new("Doc", "https://docs.example.com/a", "snip", "searxng", 2),
                new("Wiki", "https://en.wikipedia.org/wiki/A", "snip2", "wikipedia", 1)
            },
            new List<EvidencePage>
            {
                new("https://docs.example.com/a", "Doc", "text", "searxng")
            },
            new List<EvidencePassage>
            {
                new(1, "https://docs.example.com/a", "Doc", "searxng", "passage text", 4.5)
            },
            new SearchHealth(true, SearchHealthStatus.Healthy, "ok"),
            new SearchDiagnostics(2, 1, 1, 1, 1, 1, 2, new List<string> { "stage1=Healthy" }));

        var sources = composer.BuildSourcesBlock(bundle, 8);
        var evidence = composer.BuildEvidenceBlock(bundle, 8);

        Assert.Contains("https://docs.example.com/a", sources);
        Assert.Contains("passage text", evidence);
        Assert.Contains("Source: searxng", evidence);
    }
}
