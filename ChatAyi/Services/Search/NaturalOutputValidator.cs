using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ChatAyi.Services.Search;

public static class NaturalOutputValidator
{
    public static string ValidateAndCleanSearchOutput(
        string raw,
        IReadOnlyList<FreeSearchClient.SearchResult> results,
        IReadOnlyList<BrowseClient.BrowsePage> browsedPages,
        SearchGroundingBundle grounding = null)
    {
        var text = (raw ?? string.Empty).Trim();

        // Gate 1: Empty output → honest fallback.
        if (text.Length == 0)
            return "Maaf, gua belum bisa nemuin jawaban yang cukup grounded dari sumber yang ada.";

        // Gate 2: Strip residual template tags + stale markers.
        text = StripResidualTemplateTags(text);

        // Gate 3: Ensure third-person perspective (prevent identity contamination).
        text = EnsureThirdPerson(text);

        // Gate 4: Minimum quality check — too short means LLM likely refused or broke.
        if (text.Length < 40)
        {
            var sourceUrls = BuildCompactSourceUrls(results, browsedPages, maxSources: 3);
            var sourceLine = sourceUrls.Count > 0
                ? "\n\n📎 Sumber yang gua temuin: " + string.Join(", ", sourceUrls)
                : string.Empty;
            return "Gua nemuin beberapa sumber, tapi datanya belum cukup buat kasih jawaban yang solid. "
                 + "Coba query yang lebih spesifik atau pakai /browse <url> langsung."
                 + sourceLine;
        }

        // Gate 5: Suspicious pattern detection — catch common LLM hallucination tells.
        text = FlagSuspiciousPatterns(text);

        // Gate 6: Evidence grounding cross-check.
        if (grounding?.Passages is { Count: > 0 })
        {
            var groundingScore = ComputeGroundingOverlap(text, grounding.Passages);
            Debug.WriteLine($"[SearchValidation] grounding-overlap={groundingScore:F2}");

            if (groundingScore < 0.10)
            {
                // LLM output shares almost no words with evidence → likely hallucinated.
                text = "⚠️ Perlu dicatat: jawaban di bawah ini mungkin kurang akurat karena gua "
                     + "deteksi bahwa kontennya kurang cocok sama bukti yang berhasil dikumpulin.\n\n"
                     + text;
            }
        }

        // Gate 7: Ensure source attribution exists.
        text = EnsureSourceAttribution(text, results, browsedPages);

        // Final cleanup: remove excessive blank lines.
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.TrimEnd();
    }

    /// <summary>
    /// Strips leftover template tags that the LLM may emit despite being told not to.
    /// </summary>
    private static string StripResidualTemplateTags(string text)
    {
        var tags = new[] { "[FAKTA]", "[INFERENSI]", "[RINGKASAN]", "[POIN PENTING]", "[SUMBER]" };
        foreach (var tag in tags)
            text = text.Replace(tag, string.Empty, StringComparison.OrdinalIgnoreCase);

        // Also strip numbered source blocks like "[1] http..." if they appear in a raw dump style.
        // Keep them only if they're inline citations within natural prose.
        var lines = text.Split('\n');
        var cleaned = new List<string>();
        var sourceBlockStarted = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Detect a standalone "Sumber:" line that starts a source block dump.
            if (trimmed.Equals("Sumber:", StringComparison.OrdinalIgnoreCase))
            {
                sourceBlockStarted = true;
                continue;
            }
            // Inside a source block, skip numbered URL lines like "[1] https://..."
            if (sourceBlockStarted && System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\[\d+\]\s*https?://"))
                continue;
            // Any non-matching line exits the source block.
            sourceBlockStarted = false;
            cleaned.Add(line);
        }
        return string.Join('\n', cleaned).Trim();
    }

    /// <summary>
    /// Detects suspicious LLM hallucination patterns and appends natural warnings.
    /// </summary>
    private static string FlagSuspiciousPatterns(string text)
    {
        // Common tells that the LLM is making things up:
        var suspiciousPatterns = new[]
        {
            ("menurut laporan terbaru", "menurut beberapa sumber"),
            ("dilansir dari berbagai sumber", "berdasarkan sumber yang gua temuin"),
            ("as of my last update", ""),
            ("as of my knowledge cutoff", ""),
            ("I don't have real-time", ""),
            ("my training data", ""),
        };

        foreach (var (pattern, replacement) in suspiciousPatterns)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(replacement))
                    text = text.Replace(pattern, replacement, StringComparison.OrdinalIgnoreCase);
                else
                    text = text.Replace(pattern, string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        }

        return text;
    }

    /// <summary>
    /// Computes token-level overlap between LLM output text and evidence passages.
    /// Returns a ratio 0.0–1.0 indicating how grounded the output is.
    /// </summary>
    private static double ComputeGroundingOverlap(
        string outputText,
        IReadOnlyList<EvidencePassage> passages)
    {
        if (string.IsNullOrWhiteSpace(outputText) || passages is null || passages.Count == 0)
            return 0;

        var outputTokens = TokenizeForGrounding(outputText);
        if (outputTokens.Count == 0)
            return 0;

        // Build union of all evidence tokens.
        var evidenceTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in passages)
        {
            foreach (var token in TokenizeForGrounding(p.Text))
                evidenceTokens.Add(token);
            foreach (var token in TokenizeForGrounding(p.Title))
                evidenceTokens.Add(token);
        }

        if (evidenceTokens.Count == 0)
            return 0;

        // Count how many output content tokens appear in evidence.
        var matchCount = outputTokens.Count(t => evidenceTokens.Contains(t));
        return (double)matchCount / outputTokens.Count;
    }

    /// <summary>
    /// Tokenizes text into meaningful words for grounding overlap comparison.
    /// Filters out common Indonesian and English stop words to focus on content tokens.
    /// </summary>
    private static HashSet<string> TokenizeForGrounding(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Indonesian stop words
            "yang", "dan", "di", "dari", "untuk", "dengan", "ini", "itu",
            "adalah", "pada", "ke", "dalam", "oleh", "juga", "akan", "tidak",
            "ada", "atau", "saat", "jadi", "kalo", "kalau", "tapi", "gua",
            "gue", "lu", "udah", "aja", "sih", "dong", "nih", "deh",
            "bisa", "sama", "lagi", "punya", "mau", "buat", "satu",
            "dia", "mereka", "kita", "kami", "saya", "aku",
            // English stop words
            "the", "is", "at", "of", "and", "a", "an", "in", "to",
            "for", "on", "by", "with", "as", "was", "were", "been",
            "be", "are", "it", "its", "or", "but", "not", "this",
            "that", "from", "has", "have", "had", "he", "she", "his",
            "her", "their", "which", "who", "whom"
        };

        return System.Text.RegularExpressions.Regex.Matches(
                text.ToLowerInvariant(), "[a-z0-9]{3,}")
            .Select(m => m.Value)
            .Where(t => !stopWords.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures the output includes source attribution.
    /// If the LLM forgot to add sources, appends a compact source footer.
    /// </summary>
    private static string EnsureSourceAttribution(
        string text,
        IReadOnlyList<FreeSearchClient.SearchResult> results,
        IReadOnlyList<BrowseClient.BrowsePage> browsedPages)
    {
        var hasSourceMention = text.Contains("sumber", StringComparison.OrdinalIgnoreCase)
                            || text.Contains("📎", StringComparison.Ordinal)
                            || text.Contains("http", StringComparison.OrdinalIgnoreCase);

        if (!hasSourceMention)
        {
            var sourceUrls = BuildCompactSourceUrls(results, browsedPages, maxSources: 5);
            if (sourceUrls.Count > 0)
            {
                text += "\n\n📎 Sumber: " + string.Join(", ", sourceUrls);
            }
        }

        return text;
    }

    private static List<string> BuildCompactSourceUrls(
        IReadOnlyList<FreeSearchClient.SearchResult> results,
        IReadOnlyList<BrowseClient.BrowsePage> browsedPages,
        int maxSources)
    {
        var outList = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string rawUrl)
        {
            if (outList.Count >= maxSources)
                return;

            if (!Uri.TryCreate(rawUrl ?? string.Empty, UriKind.Absolute, out var uri))
                return;

            if (uri.Scheme is not ("http" or "https"))
                return;

            var key = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (!seen.Add(key))
                return;

            outList.Add(uri.ToString());
        }

        foreach (var page in (browsedPages ?? Array.Empty<BrowseClient.BrowsePage>()).OrderBy(x => SearchUrlHelpers.IsWikipediaUrl(x?.Url) ? 1 : 0))
            TryAdd(page?.Url);

        foreach (var result in (results ?? Array.Empty<FreeSearchClient.SearchResult>()).OrderBy(x => SearchUrlHelpers.IsWikipediaUrl(x?.Url) ? 1 : 0))
            TryAdd(result?.Url);

        return outList;
    }

    public static string EnsureThirdPerson(string line)
    {
        var value = (line ?? string.Empty).Trim();
        if (value.Length == 0)
            return value;

        value = value.Replace("Gua ", "Subjek ini ", StringComparison.OrdinalIgnoreCase)
                     .Replace("Gue ", "Subjek ini ", StringComparison.OrdinalIgnoreCase)
                     .Replace("Aku ", "Subjek ini ", StringComparison.OrdinalIgnoreCase)
                     .Replace("Saya ", "Subjek ini ", StringComparison.OrdinalIgnoreCase);
        return value;
    }
}
