using CityPortal.News;

namespace CityPortal.IntegrationTests;

public sealed class ArticleUrlTests
{
    [Theory]
    [InlineData("Тест 2", "/news/test-2-17-11-2026-abcdef")]
    [InlineData("Ёж, щука!", "/news/yozh-shchuka-17-11-2026-abcdef")]
    [InlineData("!!!", "/news/article-17-11-2026-abcdef")]
    [InlineData("Подъезд", "/news/podezd-17-11-2026-abcdef")]
    [InlineData("A_日本 B", "/news/a-b-17-11-2026-abcdef")]
    [InlineData("День", "/news/den-17-11-2026-abcdef")]
    public void Address_TransliteratesAndFormats(string title, string expected)
    {
        Assert.Equal(expected, ArticleUrls.Address(title, new DateOnly(2026, 11, 17), Guid.Parse("00000000-0000-0000-0000-000000abcdef")));
    }

    [Fact]
    public void Address_LimitsTitleWithoutSplittingTransliteration()
    {
        string address = ArticleUrls.Address(new string('a', 119) + "щ", new DateOnly(2026, 11, 17), Guid.Empty);
        Assert.Equal($"/news/{new string('a', 119)}-17-11-2026-000000", address);
    }
}
