using ChatAyi.Services.Search;

namespace ChatAyi.Search.Tests;

public sealed class SearchIntentClassifierTests
{
    private readonly SearchIntentClassifier _sut = new();

    [Fact]
    public void Classify_PersonEntity_Query()
    {
        var intent = _sut.Classify("windah basudara");
        Assert.Equal(SearchIntent.PersonEntity, intent);
    }

    [Fact]
    public void Classify_CodeRepo_Query()
    {
        var intent = _sut.Classify("chat ayi github repo");
        Assert.Equal(SearchIntent.CodeRepo, intent);
    }

    [Fact]
    public void Classify_Documentation_Query()
    {
        var intent = _sut.Classify("dotnet maui docs");
        Assert.Equal(SearchIntent.Documentation, intent);
    }

    [Fact]
    public void Classify_Openclaw_IsNotCodeRepoByDefault()
    {
        var intent = _sut.Classify("openclaw");
        Assert.NotEqual(SearchIntent.CodeRepo, intent);
    }
}
