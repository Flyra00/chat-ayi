using System.Net;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatAyi.Services;

public sealed class BrowseClient
{
    private const int MaxBrowseChars = 20000;
    private readonly HttpClient _http;

    public BrowseClient(HttpClient http)
    {
        _http = http;
    }

    public sealed record BrowsePage(string Url, string Title, string Text);

    public async Task<BrowsePage> FetchAsync(string url, CancellationToken ct)
    {
        if (!TryNormalizeUrl(url, out var uri))
            throw new ArgumentException("Invalid URL");

        Debug.WriteLine($"[BrowseFlow] start url={uri}");

        if (TryParseGithubRepoRoot(uri, out var owner, out var repo))
        {
            var repoReadme = await TryFetchGithubRepoReadmeAsync(owner, repo, ct);
            if (repoReadme is not null)
            {
                Debug.WriteLine($"[BrowseFlow] path=github-readme success repo={owner}/{repo}");
                return repoReadme;
            }
            Debug.WriteLine($"[BrowseFlow] path=github-readme fallback-to-normal repo={owner}/{repo}");
        }

        var jinaPage = await TryFetchWithJinaAsync(uri, ct);
        if (jinaPage is not null)
        {
            Debug.WriteLine($"[BrowseFlow] path=jina success url={uri}");
            return jinaPage;
        }

        Debug.WriteLine($"[BrowseFlow] path=fallback-http url={uri}");

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "text/html, text/plain, application/json;q=0.9, */*;q=0.8");

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.Message;
            throw new HttpRequestException(
                $"Connection failure for URL '{uri}': {(inner ?? ex.Message)}",
                ex);
        }
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? string.Empty;

        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(string.IsNullOrWhiteSpace(err) ? $"HTTP {(int)resp.StatusCode}" : err);
        }

        var bytes = await ReadWithLimitAsync(resp, limitBytes: 1_000_000, ct);
        var raw = Encoding.UTF8.GetString(bytes);

        var finalUrl = resp.RequestMessage?.RequestUri?.ToString() ?? uri.ToString();

        if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) || LooksLikeHtml(raw))
        {
            var title = ExtractTitle(raw);
            var text = HtmlToText(raw);
            if (text.Length < 80)
                throw new InvalidOperationException("No readable page content extracted.");
            if (LooksLikeBlockedOrNoisy(text))
                throw new InvalidOperationException("Page content appears blocked or noisy.");

            text = Truncate(text, MaxBrowseChars);
            Debug.WriteLine($"[BrowseFlow] fallback parse=html chars={text.Length} url={finalUrl}");
            return new BrowsePage(finalUrl, title, text);
        }

        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var text = NormalizeWhitespace(raw);
            if (text.Length < 40)
                throw new InvalidOperationException("Content is empty or unsupported for grounding.");
            if (LooksLikeBlockedOrNoisy(text))
                throw new InvalidOperationException("Page content appears blocked or noisy.");

            text = Truncate(text, MaxBrowseChars);
            Debug.WriteLine($"[BrowseFlow] fallback parse=text chars={text.Length} url={finalUrl}");
            return new BrowsePage(finalUrl, string.Empty, text);
        }

        throw new InvalidOperationException($"Unsupported content-type: {contentType}");
    }

    private async Task<BrowsePage> TryFetchWithJinaAsync(Uri originalUri, CancellationToken ct)
    {
        var jinaUrl = "https://r.jina.ai/" + originalUri.AbsoluteUri;

        using var req = new HttpRequestMessage(HttpMethod.Get, jinaUrl);
        req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
        req.Headers.TryAddWithoutValidation("Accept", "text/plain, text/markdown;q=0.9, */*;q=0.8");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        }
        catch
        {
            return null;
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[BrowseFlow] jina failed status={(int)resp.StatusCode} url={originalUri}");
                return null;
            }

            var bytes = await ReadWithLimitAsync(resp, limitBytes: 1_000_000, timeoutCts.Token);
            var raw = Encoding.UTF8.GetString(bytes);
            var text = NormalizeWhitespace(raw);
            if (!IsReadableEnough(text, minChars: 80))
            {
                Debug.WriteLine($"[BrowseFlow] jina rejected reason=not-readable url={originalUri}");
                return null;
            }
            if (LooksLikeBlockedOrNoisy(text))
            {
                Debug.WriteLine($"[BrowseFlow] jina rejected reason=blocked-or-noisy url={originalUri}");
                return null;
            }

            var title = ExtractReadableTitle(text);
            Debug.WriteLine($"[BrowseFlow] jina extracted chars={text.Length} url={originalUri}");
            return new BrowsePage(originalUri.ToString(), title, Truncate(text, MaxBrowseChars));
        }
    }

    private async Task<BrowsePage> TryFetchGithubRepoReadmeAsync(string owner, string repo, CancellationToken ct)
    {
        try
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/readme";
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", "ChatAyi/1.0");
            req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github.raw");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            if (!resp.IsSuccessStatusCode)
                return null;

            var raw = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
            var text = NormalizeWhitespace(raw);
            if (!IsReadableEnough(text, minChars: 80) || LooksLikeBlockedOrNoisy(text))
                return null;

            var repoUrl = $"https://github.com/{owner}/{repo}";
            var title = $"{owner}/{repo} README";
            return new BrowsePage(repoUrl, title, Truncate(text, MaxBrowseChars));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseGithubRepoRoot(Uri uri, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;

        if (uri is null)
            return false;

        var host = (uri.Host ?? string.Empty).Trim().ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal))
            host = host.Substring(4);

        if (!host.Equals("github.com", StringComparison.Ordinal))
            return false;

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();

        if (segments.Length != 2)
            return false;

        owner = segments[0];
        repo = segments[1];
        return owner.Length > 0 && repo.Length > 0;
    }

    private static bool TryNormalizeUrl(string raw, out Uri uri)
    {
        uri = null;
        var s = (raw ?? string.Empty).Trim();
        if (s.Length == 0) return false;

        // Trim common wrappers users paste.
        s = s.Trim().Trim('"', '\'', '<', '>', '(', ')', '[', ']', '{', '}', ',', '.');

        // If user pasted without scheme, assume https.
        if (!s.Contains("://", StringComparison.Ordinal))
            s = "https://" + s;

        if (!Uri.TryCreate(s, UriKind.Absolute, out uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        return !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static async Task<byte[]> ReadWithLimitAsync(HttpResponseMessage resp, int limitBytes, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
            if (read <= 0) break;

            if (ms.Length + read > limitBytes)
            {
                var allowed = (int)Math.Max(0, limitBytes - ms.Length);
                if (allowed > 0)
                    ms.Write(buffer, 0, allowed);
                break;
            }

            ms.Write(buffer, 0, read);
        }

        return ms.ToArray();
    }

    private static bool LooksLikeHtml(string s)
    {
        var t = (s ?? string.Empty).TrimStart();
        return t.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
               || t.Contains("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractTitle(string html)
    {
        var m = Regex.Match(html ?? string.Empty, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return string.Empty;
        return NormalizeWhitespace(WebUtility.HtmlDecode(m.Groups[1].Value));
    }

    private static string HtmlToText(string html)
    {
        var s = html ?? string.Empty;

        s = Regex.Replace(s, "<(script|style|noscript|svg|canvas|iframe)[\\s\\S]*?</\\1>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<(nav|header|footer|aside|form)[\\s\\S]*?</\\1>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<!--([\\s\\S]*?)-->", " ");
        s = Regex.Replace(s, "<(br|/p|/div|/h[1-6]|/li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);

        return NormalizeWhitespace(s);
    }

    private static string NormalizeWhitespace(string text)
    {
        var s = (text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        // Collapse spaces per line.
        var lines = s.Split('\n');
        for (var i = 0; i < lines.Length; i++)
            lines[i] = Regex.Replace(lines[i], "\\s+", " ").Trim();

        // Remove empty lines bursts.
        var sb = new StringBuilder();
        var empty = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                empty++;
                if (empty <= 1) sb.AppendLine();
                continue;
            }
            empty = 0;
            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }

    private static bool IsReadableEnough(string text, int minChars)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length < minChars)
            return false;

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 12;
    }

    private static string ExtractReadableTitle(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var candidate = line.Trim();
            if (candidate.Length < 6 || candidate.Length > 120)
                continue;
            if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;
            if (candidate.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
                continue;

            return candidate;
        }

        return string.Empty;
    }

    private static string Truncate(string s, int maxChars)
    {
        s ??= string.Empty;
        if (s.Length <= maxChars) return s;

        var cut = s.LastIndexOf('\n', Math.Min(maxChars, s.Length - 1));
        if (cut < maxChars / 2)
            cut = maxChars;

        return s.Substring(0, cut).TrimEnd() + "\n\n[...truncated...]";
    }

    private static bool LooksLikeBlockedOrNoisy(string text)
    {
        var value = (text ?? string.Empty).ToLowerInvariant();
        if (value.Length == 0)
            return true;

        var noisyTokens = new[]
        {
            "verify you are human",
            "captcha",
            "enable javascript",
            "attention required",
            "cloudflare",
            "access denied",
            "too many requests",
            "request blocked"
        };

        foreach (var token in noisyTokens)
        {
            if (value.Contains(token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
