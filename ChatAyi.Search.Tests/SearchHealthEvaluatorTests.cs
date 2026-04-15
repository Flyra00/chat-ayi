using ChatAyi.Services.Search;

namespace ChatAyi.Search.Tests;

public sealed class SearchHealthEvaluatorTests
{
    private readonly SearchHealthEvaluator _sut = new();

    [Fact]
    public void Evaluate_WikiOnlyEntityEvidence_IsWikiHeavy()
    {
        var pages = new List<EvidencePage>
        {
            new("https://en.wikipedia.org/wiki/Windah_Basudara", "Windah", "text" + new string('a', 500), "wikipedia"),
            new("https://id.wikipedia.org/wiki/Windah_Basudara", "Windah", "text" + new string('b', 500), "wikipedia"),
            new("https://en.wikipedia.org/wiki/Gaming", "Gaming", "text" + new string('c', 500), "wikipedia")
        };

        var passages = new List<EvidencePassage>
        {
            new(1, pages[0].Url, pages[0].Title, pages[0].Source, "p1", 1),
            new(2, pages[1].Url, pages[1].Title, pages[1].Source, "p2", 1),
            new(3, pages[2].Url, pages[2].Title, pages[2].Source, "p3", 1),
            new(4, pages[0].Url, pages[0].Title, pages[0].Source, "p4", 1),
            new(5, pages[1].Url, pages[1].Title, pages[1].Source, "p5", 1)
        };

        var health = _sut.Evaluate(SearchIntent.PersonEntity, pages, passages);
        Assert.False(health.IsHealthy);
        Assert.Equal(SearchHealthStatus.WikiHeavy, health.Status);
    }

    [Fact]
    public void Evaluate_NonWikiQuotaMet_IsHealthy()
    {
        var pages = new List<EvidencePage>
        {
            new("https://example.com/profile", "Profile", "text" + new string('a', 500), "searxng"),
            new("https://news.example.net/interview", "Interview", "text" + new string('b', 500), "jina"),
            new("https://en.wikipedia.org/wiki/Windah_Basudara", "Wiki", "text" + new string('c', 500), "wikipedia")
        };

        var passages = new List<EvidencePassage>
        {
            new(1, pages[0].Url, pages[0].Title, pages[0].Source, "p1", 3.0),
            new(2, pages[1].Url, pages[1].Title, pages[1].Source, "p2", 3.5),
            new(3, pages[2].Url, pages[2].Title, pages[2].Source, "p3", 2.0),
            new(4, pages[1].Url, pages[1].Title, pages[1].Source, "p4", 3.0),
            new(5, pages[0].Url, pages[0].Title, pages[0].Source, "p5", 2.5) // Total 5 passages, 4 non-wiki. Avg score = 2.8.
        };

        var health = _sut.Evaluate(SearchIntent.General, pages, passages);
        Assert.True(health.IsHealthy);
        Assert.Equal(SearchHealthStatus.Healthy, health.Status);
    }
}
