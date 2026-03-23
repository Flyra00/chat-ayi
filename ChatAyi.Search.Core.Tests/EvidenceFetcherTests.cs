using System.Net;
using System.Net.Http;
using System.Text;
using ChatAyi.Services;
using ChatAyi.Services.Search;

namespace ChatAyi.Search.Core.Tests;

public sealed class EvidenceFetcherTests
{
    [Fact]
    public async Task FetchAsync_TracksAttempts_AndHonorsMaxPagesPerDomain()
    {
        var http = new HttpClient(new TestHttpHandler(req =>
        {
            var host = req.RequestUri?.Host ?? string.Empty;
            if (host.Equals("r.jina.ai", StringComparison.OrdinalIgnoreCase))
            {
                var payload = "Title\n\n" + string.Join(' ', Enumerable.Repeat("evidenceword", 80));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "text/plain"),
                    RequestMessage = req
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found"),
                RequestMessage = req
            };
        }));

        var browse = new BrowseClient(http);
        var sut = new EvidenceFetcher(browse);

        var candidates = new List<SearchCandidate>
        {
            new("A", "https://same.example.com/a", "s", "searxng", 3),
            new("B", "https://same.example.com/b", "s", "searxng", 2),
            new("C", "https://other.example.net/c", "s", "jina", 1)
        };

        var result = await sut.FetchAsync(
            candidates,
            maxAttempts: 5,
            targetPages: 2,
            maxPagesPerDomain: 1,
            CancellationToken.None);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(3, result.Attempts);
        Assert.Equal(1, result.Pages.Count(p => SearchUrlHelpers.TryGetDomain(p.Url) == "same.example.com"));
    }
}
