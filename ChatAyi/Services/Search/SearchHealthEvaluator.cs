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
        var avgScore = passageCount > 0 ? passages.Average(p => p.Score) : 0;

        const int minPages = 3;
        const int minPassages = 5;
        const int minNonWikiPages = 2;
        const int minNonWikiPassages = 3;

        if (pageCount < minPages || passageCount < minPassages)
        {
            return new SearchHealth(
                false,
                SearchHealthStatus.WeakEvidence,
                $"Too few pages ({pageCount}/{minPages}) or passages ({passageCount}/{minPassages}).");
        }

        if (nonWikiPages < minNonWikiPages || nonWikiPassages < minNonWikiPassages)
        {
            return new SearchHealth(
                false,
                SearchHealthStatus.WikiHeavy,
                $"Non-wiki evidence quota not met: pages ({nonWikiPages}/{minNonWikiPages}), passages ({nonWikiPassages}/{minNonWikiPassages}).");
        }

        if (avgScore < 1.5)
        {
            return new SearchHealth(
                false,
                SearchHealthStatus.WeakEvidence,
                $"Passage count is sufficient, but average relevance score is very low ({avgScore:F1}).");
        }

        if (avgScore < 2.5)
        {
            return new SearchHealth(
                false,
                SearchHealthStatus.Marginal,
                $"Passage count is adequate, but relevance is marginal ({avgScore:F1}).");
        }

        return new SearchHealth(
            true,
            SearchHealthStatus.Healthy,
            $"Healthy evidence set (Avg Score: {avgScore:F1}).");
    }
}
