namespace ChatAyi.Services.Search;

public sealed class SearchIntentClassifier
{
    private static readonly string[] CodeTokens =
    {
        "github", "repo", "repository", "library", "sdk", "package",
        "nuget", "npm", "pip", "crate", "gem", "docs", "documentation",
        "api", "reference", "guide", "tutorial", "install", "setup",
        "c#", "dotnet", ".net", "python", "javascript", "typescript",
        "java", "golang", "rust", "go ", "react", "nextjs", "laravel"
    };

    public SearchIntent Classify(string query)
    {
        query = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (query.Length == 0)
            return SearchIntent.General;

        if (CodeTokens.Any(t => query.Contains(t, StringComparison.Ordinal)))
        {
            if (query.Contains("docs", StringComparison.Ordinal)
                || query.Contains("documentation", StringComparison.Ordinal)
                || query.Contains("api", StringComparison.Ordinal)
                || query.Contains("reference", StringComparison.Ordinal)
                || query.Contains("guide", StringComparison.Ordinal))
            {
                return SearchIntent.Documentation;
            }

            return SearchIntent.CodeRepo;
        }

        if (LooksLikePersonEntity(query))
            return SearchIntent.PersonEntity;

        return SearchIntent.General;
    }

    private static bool LooksLikePersonEntity(string query)
    {
        if (query.Contains("http", StringComparison.Ordinal)
            || query.Contains('/', StringComparison.Ordinal)
            || query.Contains("docs", StringComparison.Ordinal)
            || query.Contains("api", StringComparison.Ordinal)
            || query.Contains("repo", StringComparison.Ordinal)
            || query.Contains("github", StringComparison.Ordinal))
        {
            return false;
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 2 or > 5)
            return false;

        return tokens.All(t => t.All(ch => char.IsLetter(ch) || ch == '-' || ch == '.'));
    }
}
