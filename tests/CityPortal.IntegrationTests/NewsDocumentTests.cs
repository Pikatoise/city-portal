using System.Text.Json;
using CityPortal.Identity;
using CityPortal.News;

namespace CityPortal.IntegrationTests;

public sealed class NewsDocumentTests
{
    [Fact]
    public void Documents_IncompleteDraftAndCompleteSubmission_ApplyDifferentPresenceRules()
    {
        Assert.Equal("", ArticleDocuments.Validate(new ArticleContent()).Content.Title);
        Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(new ArticleContent(), true));
        ArticleContent content = Content("A factual report") with { Title = "Report", Summary = "Summary", CategoryId = Guid.NewGuid(), UsesOthers = true };
        Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(content, true));
        content = content with { Sources = [new ArticleSource("Official source", "https://source.example.test/report")] };
        Assert.Equal("Report", ArticleDocuments.Validate(content, true).Content.Title);
        content = content with { Cover = new ArticleImage(Guid.NewGuid()) };
        Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(content, true));
        Assert.NotNull(ArticleDocuments.Validate(content).Content.Cover);
        Assert.NotNull(ArticleDocuments.Validate(content with { Cover = content.Cover with { Alt = "The city hall" } }, true).Content.Cover);
    }

    [Fact]
    public void Documents_NfcScalarAndBlockBoundaries_RejectOnlyBeyondTheAgreedMaximum()
    {
        string emoji = string.Concat(Enumerable.Repeat("\U0001F331", 200));
        Assert.Equal(emoji, ArticleDocuments.Validate(Content("text") with { Title = emoji }).Content.Title);
        Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(Content("text") with { Title = emoji + "a" }));
        Assert.Equal("é", ArticleDocuments.Validate(Content("text") with { Title = "e\u0301" }).Content.Title);
        Assert.Equal(100000, ArticleDocuments.Validate(Content(new string('a', 100000))).PlainText.TrimEnd('\n').Length);
        Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(Content(new string('a', 100001))));
        JsonElement blocks = JsonSerializer.SerializeToElement(new { type = "doc", content = Enumerable.Range(0, 2000).Select(_ => new { type = "paragraph" }).ToArray() });
        Assert.Equal(2000, ArticleDocuments.Validate(new ArticleContent { Body = blocks }).Content.Body.GetProperty("content").GetArrayLength());
        JsonElement tooMany = JsonSerializer.SerializeToElement(new { type = "doc", content = Enumerable.Range(0, 2001).Select(_ => new { type = "paragraph" }).ToArray() });
        Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(new ArticleContent { Body = tooMany }));
    }

    [Theory]
    [InlineData("unsupported-node")]
    [InlineData("unsafe-link")]
    [InlineData("unknown-attribute")]
    [InlineData("wrong-heading-type")]
    [InlineData("wrong-nesting")]
    [InlineData("duplicate-property")]
    [InlineData("deep-document")]
    [InlineData("oversized-json")]
    public void Documents_InvalidShapesAndLinks_AreValidationErrors(string scenario)
    {
        string json = scenario switch
        {
            "unsupported-node" => "{\"type\":\"doc\",\"content\":[{\"type\":\"html\",\"text\":\"x\"}]}",
            "unsafe-link" => "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"x\",\"marks\":[{\"type\":\"link\",\"attrs\":{\"href\":\"javascript:alert(1)\"}}]}]}]}",
            "unknown-attribute" => "{\"type\":\"doc\",\"attrs\":{\"onclick\":\"alert(1)\"}}",
            "wrong-heading-type" => "{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":\"2\"}}]}",
            "wrong-nesting" => "{\"type\":\"doc\",\"content\":[{\"type\":\"text\",\"text\":\"x\"}]}",
            "duplicate-property" => "{\"type\":\"doc\",\"type\":\"doc\"}",
            "deep-document" => string.Concat(Enumerable.Repeat("{\"type\":\"blockquote\",\"content\":[", 33)) + "{\"type\":\"paragraph\"}" + string.Concat(Enumerable.Repeat("]}", 33)),
            _ => "{\"type\":\"doc\"," + new string(' ', 2 * 1024 * 1024) + "\"content\":[]}"
        };
        if (scenario == "deep-document")
            json = "{\"type\":\"doc\",\"content\":[" + json + "]}";
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
        AccountOperationException failure = Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(new ArticleContent { Body = document.RootElement }));
        Assert.Equal(400, failure.Status);
    }

    [Fact]
    public void Documents_SafeRenderingAndMediaPlacements_EscapeTextAndCountDistinctReferences()
    {
        var image = Guid.NewGuid();
        JsonElement body = JsonSerializer.SerializeToElement(new
        {
            type = "doc",
            content = new object[]
            {
                new { type = "paragraph", content = new[] { new { type = "text", text = "<script>alert(1)</script>", marks = new[] { new { type = "link", attrs = new { href = "https://example.test/?x=1&y=2", target = "_blank" } } } } } },
                new { type = "mediaFigure", attrs = new { mediaId = image, alt = "\" onerror=alert(1)", caption = "<img src=x>" } }
            }
        });
        ValidatedArticleContent valid = ArticleDocuments.Validate(new ArticleContent { Body = body, Cover = new ArticleImage(image), Gallery = Enumerable.Repeat(new ArticleImage(image), 21).ToArray() });
        Assert.Equal(image, Assert.Single(valid.ImageIds));
        Assert.Contains("&lt;script&gt;", valid.PreviewHtml);
        Assert.Contains("rel=\"noopener noreferrer\"", valid.PreviewHtml);
        Assert.DoesNotContain("<script>", valid.PreviewHtml);
        Assert.DoesNotContain("<img src=x>", valid.PreviewHtml);
        Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(new ArticleContent { Gallery = Enumerable.Range(0, 21).Select(_ => new ArticleImage(Guid.NewGuid())).ToArray() }));
        Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(Content("x") with { Sources = [new ArticleSource("unsafe", "data:text/html,unsafe")] }));
    }

    internal static ArticleContent Content(string text) => new()
    {
        Body = JsonSerializer.SerializeToElement(new { type = "doc", content = new[] { new { type = "paragraph", content = new[] { new { type = "text", text } } } } })
    };
}
