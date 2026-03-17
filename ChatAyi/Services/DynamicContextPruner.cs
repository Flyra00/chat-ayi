using System.Text;

namespace ChatAyi.Services;

public sealed class DynamicContextPruner
{
    public sealed record Message(string Role, string Content, bool IsPinned = false);

    public sealed record Options(int MaxChars, int PreserveRecentMessages, bool EnableDeduplication = true);

    public sealed record Result(
        IReadOnlyList<Message> Messages,
        int OriginalChars,
        int FinalChars,
        int RemovedCount)
    {
        public bool Pruned => RemovedCount > 0 || FinalChars < OriginalChars;
        public int SavedChars => Math.Max(0, OriginalChars - FinalChars);
    }

    public Result Prune(IReadOnlyList<Message> input, Options options)
    {
        if (input == null || input.Count == 0)
            return new Result(Array.Empty<Message>(), 0, 0, 0);

        var work = input.Select(m => new Message(NormRole(m.Role), m.Content ?? string.Empty, m.IsPinned)).ToList();
        var originalChars = CountChars(work);
        var removed = 0;

        if (options.EnableDeduplication)
        {
            var dedup = Deduplicate(work);
            removed += work.Count - dedup.Count;
            work = dedup;
        }

        var recentProtected = BuildRecentProtectedSet(work, options.PreserveRecentMessages);

        while (CountChars(work) > options.MaxChars)
        {
            var idx = FindOldestRemovableIndex(work, recentProtected);
            if (idx < 0)
                break;

            work.RemoveAt(idx);
            removed++;
            recentProtected = BuildRecentProtectedSet(work, options.PreserveRecentMessages);
        }

        if (CountChars(work) > options.MaxChars)
        {
            // Last resort: trim oldest non-pinned long message.
            var trimIdx = FindTrimCandidateIndex(work, recentProtected);
            if (trimIdx >= 0)
            {
                var msg = work[trimIdx];
                var target = Math.Max(300, msg.Content.Length / 2);
                if (msg.Content.Length > target)
                {
                    work[trimIdx] = msg with { Content = msg.Content.Substring(0, target) + "\n\n[DCP: trimmed]" };
                }
            }
        }

        return new Result(work, originalChars, CountChars(work), removed);
    }

    private static List<Message> Deduplicate(List<Message> source)
    {
        var keptFromBack = new List<Message>(source.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = source.Count - 1; i >= 0; i--)
        {
            var m = source[i];
            if (m.IsPinned)
            {
                keptFromBack.Add(m);
                continue;
            }

            var sig = m.Role + "|" + NormalizeForSignature(m.Content);
            if (seen.Add(sig))
                keptFromBack.Add(m);
        }

        keptFromBack.Reverse();
        return keptFromBack;
    }

    private static HashSet<int> BuildRecentProtectedSet(List<Message> source, int preserveRecentMessages)
    {
        var protectedSet = new HashSet<int>();
        if (preserveRecentMessages <= 0) return protectedSet;

        var count = 0;
        for (var i = source.Count - 1; i >= 0 && count < preserveRecentMessages; i--)
        {
            if (source[i].Role == "system") continue;
            protectedSet.Add(i);
            count++;
        }
        return protectedSet;
    }

    private static int FindOldestRemovableIndex(List<Message> source, HashSet<int> recentProtected)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var m = source[i];
            if (m.IsPinned) continue;
            if (recentProtected.Contains(i)) continue;
            return i;
        }
        return -1;
    }

    private static int FindTrimCandidateIndex(List<Message> source, HashSet<int> recentProtected)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var m = source[i];
            if (m.IsPinned) continue;
            if (recentProtected.Contains(i)) continue;
            if ((m.Content?.Length ?? 0) > 600) return i;
        }
        return -1;
    }

    private static int CountChars(IEnumerable<Message> source)
    {
        var total = 0;
        foreach (var m in source)
            total += (m.Content?.Length ?? 0);
        return total;
    }

    private static string NormRole(string role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();
        return r is "assistant" or "user" or "system" ? r : "user";
    }

    private static string NormalizeForSignature(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;

        var sb = new StringBuilder(content.Length);
        var lastWasSpace = false;
        foreach (var ch in content)
        {
            var c = char.ToLowerInvariant(ch);
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        var norm = sb.ToString().Trim();
        return norm.Length > 500 ? norm.Substring(0, 500) : norm;
    }
}
