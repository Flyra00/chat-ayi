using ChatAyi.Services.Search;

namespace ChatAyi.Search.Tests;

public sealed class SearchUrlHelpersTests
{
    [Theory]
    [InlineData("https://en.wikipedia.org/wiki/OpenClaw")]
    [InlineData("https://id.wikipedia.org/wiki/OpenClaw")]
    public void IsWikipediaUrl_NormalizesSubdomain(string url)
    {
        Assert.True(SearchUrlHelpers.IsWikipediaUrl(url));
    }

    [Fact]
    public void IsSameUrl_StableNormalization()
    {
        var a = "https://example.com/path/";
        var b = "https://example.com/path";
        Assert.True(SearchUrlHelpers.IsSameUrl(a, b));
    }
}
