namespace ChatAyi.Services.Search;

public static class SearchUrlHelpers
{
    public static string TryGetDomain(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return string.Empty;

        return NormalizeDomainKey(uri.Host);
    }

    public static bool IsSameUrl(string a, string b)
    {
        return string.Equals(
            NormalizeUrlKey(a),
            NormalizeUrlKey(b),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWikipediaUrl(string url)
    {
        var host = TryGetDomain(url);
        return string.Equals(host, "wikipedia.org", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeDomainKey(string host)
    {
        var value = (host ?? string.Empty).Trim().ToLowerInvariant();
        if (value.StartsWith("www.", StringComparison.Ordinal))
            value = value.Substring(4);

        if (value.EndsWith(".wikipedia.org", StringComparison.Ordinal))
            value = "wikipedia.org";

        return value;
    }

    public static string NormalizeUrlKey(string url)
    {
        if (!Uri.TryCreate(url ?? string.Empty, UriKind.Absolute, out var uri))
            return (url ?? string.Empty).Trim();

        var left = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return string.IsNullOrWhiteSpace(uri.Query)
            ? left
            : left + uri.Query;
    }
}
