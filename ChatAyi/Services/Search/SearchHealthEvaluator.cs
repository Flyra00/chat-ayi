namespace ChatAyi.Services.Search;

public sealed class SearchHealthEvaluator
{
    public SearchHealth Evaluate(
        SearchIntent intent,
        IReadOnlyList<EvidencePage> pages,
        IReadOnlyList<EvidencePassage> passages)
    {
        var pageCount = pages?.Count ?? 0;
        var passageCount = passages?.Count ?? 0;

        if (pageCount == 0 || passageCount == 0)
        {
            return new SearchHealth(
                false,
                SearchHealthStatus.NoEvidence,
                "No usable pages/passages.");
        }

        var nonWikiPages = pages.Count(p => !SearchUrlHelpers.IsWikipediaUrl(p.Url));
        var nonWikiPassages = passages.Count(p => !SearchUrlHelpers.IsWikipediaUrl(p.Url));

        const int minPages = 3;
        const int minPassages = 4;
        const int minNonWikiPages = 2;
        const int minNonWikiPassages = 2;

        if (pageCount < minPages || passageCount < minPassages)
        {
            return new SearchHealth(
                false,
                SearchHealthStatus.WeakEvidence,
                "Too few pages/passages.");
        }

        if (nonWikiPages < minNonWikiPages || nonWikiPassages < minNonWikiPassages)
        {
            return new SearchHealth(
                false,
                SearchHealthStatus.WikiHeavy,
                "Non-wiki evidence quota not met.");
        }

        return new SearchHealth(
            true,
            SearchHealthStatus.Healthy,
            "Healthy evidence set.");
    }
}
