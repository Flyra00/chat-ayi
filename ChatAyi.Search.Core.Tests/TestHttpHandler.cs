using System.Net;
using System.Net.Http;

namespace ChatAyi.Search.Core.Tests;

internal sealed class TestHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public TestHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = _handler(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            RequestMessage = request,
            Content = new StringContent("not found")
        };

        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }
}
