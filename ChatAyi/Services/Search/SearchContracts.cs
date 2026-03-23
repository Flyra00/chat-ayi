namespace ChatAyi.Services.Search;

public enum SearchIntent
{
    General,
    PersonEntity,
    Documentation,
    CodeRepo
}

public enum SearchHealthStatus
{
    Healthy,
    WeakEvidence,
    WikiHeavy,
    NoEvidence
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

public sealed record SearchHealth(
    bool IsHealthy,
    SearchHealthStatus Status,
    string Reason);

public sealed record SearchDiagnostics(
    int CandidateCount,
    int NonWikiCandidateCount,
    int PageCount,
    int NonWikiPageCount,
    int PassageCount,
    int NonWikiPassageCount,
    int FetchAttempts,
    IReadOnlyList<string> Notes);

public sealed record EvidenceFetchResult(
    IReadOnlyList<EvidencePage> Pages,
    int Attempts);

public sealed record SearchGroundingBundle(
    SearchIntent Intent,
    IReadOnlyList<SearchCandidate> Candidates,
    IReadOnlyList<EvidencePage> Pages,
    IReadOnlyList<EvidencePassage> Passages,
    SearchHealth Health,
    SearchDiagnostics Diagnostics);
