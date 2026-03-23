using System.Text;

namespace ChatAyi.Services.Search;

public sealed class SearchGroundingComposer
{
    public string BuildSourcesBlock(SearchGroundingBundle bundle, int maxSources = 8)
    {
        var sb = new StringBuilder();

        var passageSources = (bundle?.Passages ?? Array.Empty<EvidencePassage>())
            .Select(p => new SearchCandidate(p.Title, p.Url, string.Empty, p.Source, p.Score));

        var sources = passageSources
            .Concat((bundle?.Pages ?? Array.Empty<EvidencePage>())
            .Select(p => new SearchCandidate(p.Title, p.Url, string.Empty, p.Source, 0))
            )
            .Concat(bundle?.Candidates ?? Array.Empty<SearchCandidate>())
            .GroupBy(x => SearchUrlHelpers.NormalizeUrlKey(x.Url), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(x => x.Score)
            .Take(maxSources)
            .ToList();

        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            sb.Append('[').Append(i + 1).Append("] ").AppendLine(s.Title);
            sb.AppendLine(s.Url);
            if (!string.IsNullOrWhiteSpace(s.Snippet))
                sb.AppendLine(s.Snippet);
            if (!string.IsNullOrWhiteSpace(s.Source))
                sb.AppendLine("Source: " + s.Source);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    public string BuildEvidenceBlock(SearchGroundingBundle bundle, int maxPassages = 8)
    {
        var sb = new StringBuilder();

        foreach (var p in (bundle?.Passages ?? Array.Empty<EvidencePassage>()).Take(maxPassages))
        {
            sb.Append('[').Append(p.Index).Append("] ").AppendLine(p.Title);
            sb.AppendLine(p.Url);
            sb.AppendLine("Source: " + p.Source);
            sb.AppendLine();
            sb.AppendLine(p.Text);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }
}
