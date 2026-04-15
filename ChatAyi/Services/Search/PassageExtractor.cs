using System.Text.RegularExpressions;

namespace ChatAyi.Services.Search;

public sealed class PassageExtractor
{
    public IReadOnlyList<EvidencePassage> Extract(
        string query,
        IReadOnlyList<EvidencePage> pages,
        int maxPerPage = 2,
        int maxTotal = 10)
    {
        var outList = new List<EvidencePassage>();
        var q = Tokenize(query);
        var isRecencySensitive = IsRecencySensitive(query);

        var passageIndex = 1;
        foreach (var page in pages ?? Array.Empty<EvidencePage>())
        {
            var ranked = SplitPassages(page.Text)
                .Select(p => new
                {
                    Text = p,
                    Score = ScorePassage(q, query, page, p, isRecencySensitive)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(maxPerPage)
                .ToList();

            foreach (var item in ranked)
            {
                outList.Add(new EvidencePassage(
                    passageIndex++,
                    page.Url,
                    page.Title,
                    page.Source,
                    item.Text,
                    item.Score));

                if (outList.Count >= maxTotal)
                    return outList;
            }
        }

        return outList
            .OrderByDescending(x => x.Score)
            .Take(maxTotal)
            .ToList();
    }

    private static IEnumerable<string> SplitPassages(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var blocks = Regex.Split(text, @"\n\s*\n");
        foreach (var block in blocks)
        {
            var cleaned = Regex.Replace(block, @"\s+", " ").Trim();
            if (cleaned.Length < 120)
                continue;

            if (cleaned.Length <= 650)
            {
                yield return cleaned;
                continue;
            }

            var sentences = Regex.Split(cleaned, @"(?<=[\.\!\?])\s+");
            var buffer = new List<string>();
            var len = 0;

            foreach (var sentence in sentences)
            {
                var s = sentence.Trim();
                if (s.Length == 0)
                    continue;

                if (len + s.Length > 650 && buffer.Count > 0)
                {
                    var chunk = string.Join(" ", buffer).Trim();
                    if (chunk.Length >= 120)
                        yield return chunk;

                    buffer.Clear();
                    len = 0;
                }

                buffer.Add(s);
                len += s.Length + 1;
            }

            if (buffer.Count > 0)
            {
                var chunk = string.Join(" ", buffer).Trim();
                if (chunk.Length >= 120)
                    yield return chunk;
            }
        }
    }

    private static double ScorePassage(
        HashSet<string> queryTokens,
        string rawQuery,
        EvidencePage page,
        string passage,
        bool isRecencySensitive)
    {
        var score = 0d;
        var text = (page.Title + " " + passage).ToLowerInvariant();

        var raw = (rawQuery ?? string.Empty).Trim().ToLowerInvariant();
        if (raw.Length > 0 && text.Contains(raw, StringComparison.Ordinal))
            score += 6;

        var title = (page.Title ?? string.Empty).ToLowerInvariant();
        if (raw.Length > 0 && title.Contains(raw, StringComparison.Ordinal))
            score += 0.5;

        var passageTokens = Tokenize(text);
        var overlap = queryTokens.Count == 0
            ? 0
            : queryTokens.Count(t => passageTokens.Contains(t));

        score += overlap * 1.5;

        if (!SearchUrlHelpers.IsWikipediaUrl(page.Url))
            score += 1.0;

        if (page.Source.Equals("searxng", StringComparison.OrdinalIgnoreCase))
            score += 0.25;

        if (isRecencySensitive)
        {
            var years = Regex.Matches(text, @"\b(20[0-9]{2}|19[0-9]{2})\b")
                .Select(m => int.Parse(m.Value))
                .ToList();

            if (years.Count > 0)
            {
                var maxYear = years.Max();
                
                // Recency bonus logic: newer years get higher score
                if (maxYear >= 2026)
                {
                    score += 4.0;
                    System.Diagnostics.Debug.WriteLine($"[PassageExtractor] Recency bonus applied: +4.00 (year {maxYear})");
                }
                else if (maxYear == 2025)
                {
                    score += 3.0;
                    System.Diagnostics.Debug.WriteLine($"[PassageExtractor] Recency bonus applied: +3.00 (year {maxYear})");
                }
                else if (maxYear == 2024)
                {
                    score += 1.5;
                    System.Diagnostics.Debug.WriteLine($"[PassageExtractor] Recency bonus applied: +1.50 (year {maxYear})");
                }
            }
        }

        return score;
    }

    private static bool IsRecencySensitive(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;
        var q = query.ToLowerInvariant();

        return q.Contains("saat ini") || 
               q.Contains("sekarang") || 
               q.Contains("terbaru") || 
               q.Contains("hari ini") ||
               q.Contains("current") || 
               q.Contains("latest") || 
               q.Contains("now") ||
               Regex.IsMatch(q, @"\b202[0-9]\b");
    }

    private static HashSet<string> Tokenize(string text)
    {
        text = (text ?? string.Empty).ToLowerInvariant();
        return Regex.Matches(text, "[a-z0-9]{2,}")
            .Select(m => m.Value)
            .Where(x => x.Length >= 2)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
