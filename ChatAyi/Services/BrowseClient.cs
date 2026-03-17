using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatAyi.Services;

public sealed class BrowseClient
{
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
            text = Truncate(text, 12000);
            return new BrowsePage(finalUrl, title, text);
        }

        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var text = NormalizeWhitespace(raw);
            text = Truncate(text, 12000);
            return new BrowsePage(finalUrl, string.Empty, text);
        }

        throw new InvalidOperationException($"Unsupported content-type: {contentType}");
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

    private static string Truncate(string s, int maxChars)
    {
        s ??= string.Empty;
        if (s.Length <= maxChars) return s;
        return s.Substring(0, maxChars) + "\n\n[...truncated...]";
    }
}
