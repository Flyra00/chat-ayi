using System.Net;
using System.Net.Http;
using System.Text;
using ChatAyi.Services;
using ChatAyi.Services.Search;

namespace ChatAyi.Search.Core.Tests;

public sealed class SearchProviderMuxTests
{
    [Fact]
    public async Task SearchCandidatesAsync_UsesJinaBooster_WhenGeneralPoolStillWeak()
    {
        var searxJson = BuildSearxJson(new[]
        {
            ("Wiki", "https://en.wikipedia.org/wiki/Openclaw"),
            ("News 1", "https://news-a.example.com/a"),
            ("News 2", "https://news-b.example.com/a"),
            ("News 1b", "https://news-a.example.com/b")
        });

        var jinaText = "[Booster](https://booster.example.com/openclaw)\nextra context";
        var mux = CreateMux(searxJson, jinaText, BuildGitHubJson(Array.Empty<(string, string, string)>()), BuildWikiJson(Array.Empty<(string, string, string)>()));

        var results = await mux.SearchCandidatesAsync("openclaw", 10, SearchIntent.General, CancellationToken.None);

        Assert.Contains(results, r => r.Url.Contains("booster.example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SearchCandidatesAsync_PrunesPerDomainAndWikipediaCount()
    {
        var searxJson = BuildSearxJson(new[]
        {
            ("A1", "https://example.com/a1"),
            ("A2", "https://example.com/a2"),
            ("A3", "https://example.com/a3"),
            ("W1", "https://en.wikipedia.org/wiki/A"),
            ("W2", "https://id.wikipedia.org/wiki/A"),
            ("B1", "https://other.example.net/b1")
        });

        var mux = CreateMux(searxJson, string.Empty, BuildGitHubJson(Array.Empty<(string, string, string)>()), BuildWikiJson(Array.Empty<(string, string, string)>()));
        var results = await mux.SearchCandidatesAsync("someone", 10, SearchIntent.PersonEntity, CancellationToken.None);

        var byDomain = results
            .GroupBy(r => SearchUrlHelpers.TryGetDomain(r.Url), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.True(byDomain.GetValueOrDefault("example.com") <= 2);
        Assert.True(results.Count(r => SearchUrlHelpers.IsWikipediaUrl(r.Url)) <= 1);
    }

    [Fact]
    public async Task SearchCandidatesAsync_DoesNotUseGitHub_ForPersonEntityIntent()
    {
        var searxJson = BuildSearxJson(new[]
        {
            ("Wiki", "https://en.wikipedia.org/wiki/Windah_Basudara")
        });

        var githubJson = BuildGitHubJson(new[]
        {
            ("org/repo", "https://github.com/org/repo", "desc")
        });

        var mux = CreateMux(searxJson, string.Empty, githubJson, BuildWikiJson(Array.Empty<(string, string, string)>()));
        var results = await mux.SearchCandidatesAsync("windah basudara", 10, SearchIntent.PersonEntity, CancellationToken.None);

        Assert.DoesNotContain(results, r => SearchUrlHelpers.TryGetDomain(r.Url) == "github.com");
    }

    private static SearchProviderMux CreateMux(string searxJson, string jinaText, string githubJson, string wikiJson)
    {
        var searxHttp = new HttpClient(new TestHttpHandler(req =>
        {
            if (req.RequestUri?.Host == "searx.local")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(searxJson, Encoding.UTF8, "application/json"),
                    RequestMessage = req
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found"),
                RequestMessage = req
            };
        }));

        var muxHttp = new HttpClient(new TestHttpHandler(req =>
        {
            var host = req.RequestUri?.Host ?? string.Empty;
            if (host.Equals("s.jina.ai", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(jinaText ?? string.Empty, Encoding.UTF8, "text/plain"),
                    RequestMessage = req
                };
            }

            if (host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(githubJson, Encoding.UTF8, "application/json"),
                    RequestMessage = req
                };
            }

            if (host.Equals("en.wikipedia.org", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(wikiJson, Encoding.UTF8, "application/json"),
                    RequestMessage = req
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found"),
                RequestMessage = req
            };
        }));

        var searxng = new SearxngSearchClient(searxHttp, "https://searx.local");
        return new SearchProviderMux(searxng, muxHttp, ddgFallback: null);
    }

    private static string BuildSearxJson(IEnumerable<(string title, string url)> rows)
    {
        var items = rows.Select(r =>
            $"{{\"title\":\"{Escape(r.title)}\",\"url\":\"{Escape(r.url)}\",\"content\":\"{Escape(r.title)} content\"}}");
        return "{\"results\":[" + string.Join(',', items) + "]}";
    }

    private static string BuildGitHubJson(IEnumerable<(string name, string url, string desc)> rows)
    {
        var items = rows.Select(r =>
            $"{{\"full_name\":\"{Escape(r.name)}\",\"html_url\":\"{Escape(r.url)}\",\"description\":\"{Escape(r.desc)}\"}}");
        return "{\"items\":[" + string.Join(',', items) + "]}";
    }

    private static string BuildWikiJson(IEnumerable<(string title, string desc, string url)> rows)
    {
        var list = rows.ToList();
        var titles = "[" + string.Join(',', list.Select(x => '"' + Escape(x.title) + '"')) + "]";
        var descs = "[" + string.Join(',', list.Select(x => '"' + Escape(x.desc) + '"')) + "]";
        var urls = "[" + string.Join(',', list.Select(x => '"' + Escape(x.url) + '"')) + "]";
        return "[\"q\"," + titles + "," + descs + "," + urls + "]";
    }

    private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}
