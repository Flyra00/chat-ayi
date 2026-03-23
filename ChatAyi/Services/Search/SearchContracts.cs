namespace ChatAyi.Services.Search;

public enum SearchIntent
{
    General,
    PersonEntity,
    Documentation,
    CodeRepo
}

public sealed record SearchCandidate(
    string Title,
    string Url,
    string Snippet,
    string Source,
    double Score = 0);

public sealed record EvidencePage(
    string Url,
    string Title,
    string Text,
    string Source);

public sealed record EvidencePassage(
    int Index,
    string Url,
    string Title,
    string Source,
    string Text,
    double Score);

public sealed record SearchGroundingBundle(
    SearchIntent Intent,
    IReadOnlyList<SearchCandidate> Candidates,
    IReadOnlyList<EvidencePage> Pages,
    IReadOnlyList<EvidencePassage> Passages);
