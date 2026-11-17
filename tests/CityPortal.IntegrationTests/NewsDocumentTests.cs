using System.Text.Json;
using CityPortal.Identity;
using CityPortal.News;

namespace CityPortal.IntegrationTests;

public sealed class NewsDocumentTests
{
    [Fact]
    public void Documents_EditorLinkAttributes_AreAcceptedAndCanonicalized()
    {
        using var body = JsonDocument.Parse("""
            {"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Example","marks":[{"type":"link","attrs":{"href":"https://example.test","target":"_blank","rel":"noopener noreferrer nofollow","class":null,"title":null}}]}]}]}
            """);
        ValidatedArticleContent valid = ArticleDocuments.Validate(new ArticleContent { Body = body.RootElement });
        JsonElement attributes = valid.Content.Body.GetProperty("content")[0].GetProperty("content")[0].GetProperty("marks")[0].GetProperty("attrs");
        Assert.Equal("https://example.test", attributes.GetProperty("href").GetString());
        Assert.Single(attributes.EnumerateObject());
        Assert.Contains("href=\"https://example.test\"", valid.PreviewHtml);
        Assert.Equal(valid.Content.Body.GetRawText(), ArticleDocuments.Validate(valid.Content).Content.Body.GetRawText());
    }

    [Theory]
    [InlineData("bold")]
    [InlineData("italic")]
    [InlineData("link")]
    public void Documents_FormattedHardBreak_RoundTripsEditorContent(string mark)
    {
        using var body = JsonDocument.Parse("""
            {"type":"doc","content":[{"type":"paragraph","content":[
            {"type":"text","text":"Before"},
            {"type":"hardBreak","marks":[MARK]},
            {"type":"text","text":"After"}]}]}
            """.Replace("MARK", mark == "link" ? "{\"type\":\"link\",\"attrs\":{\"href\":\"https://example.test\",\"target\":\"_blank\"}}" : JsonSerializer.Serialize(new { type = mark }), StringComparison.Ordinal));
        ValidatedArticleContent valid = ArticleDocuments.Validate(new ArticleContent { Body = body.RootElement });
        Assert.Equal("Before\nAfter\n", valid.PlainText);
        Assert.Contains("<br>", valid.PreviewHtml);
        JsonElement lineBreak = valid.Content.Body.GetProperty("content")[0].GetProperty("content")[1];
        Assert.Equal(mark, lineBreak.GetProperty("marks")[0].GetProperty("type").GetString());
        Assert.Equal(valid.Content.Body.GetRawText(), ArticleDocuments.Validate(valid.Content).Content.Body.GetRawText());
        if (mark == "link")
        {
            Assert.DoesNotContain("target", lineBreak.GetRawText());
            Assert.Contains("noopener noreferrer", valid.PreviewHtml);
        }
    }

    [Theory]
    [InlineData("unsafe-link")]
    [InlineData("unsupported-mark")]
    public void Documents_HardBreakMarks_PreserveSecurityValidation(string scenario)
    {
        string mark = scenario == "unsafe-link" ? "{\"type\":\"link\",\"attrs\":{\"href\":\"javascript:alert(1)\"}}" : "{\"type\":\"code\"}";
        using var body = JsonDocument.Parse("{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"hardBreak\",\"marks\":[" + mark + "]}]}]}");
        AccountOperationException failure = Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(new ArticleContent { Body = body.RootElement }));
        Assert.Equal(400, failure.Status);
        Assert.Equal(scenario == "unsafe-link" ? "link" : "body", Assert.Single(failure.Errors!).Key);
    }

    [Fact]
    public void Documents_MissingSubmissionFields_ReportEachField()
    {
        AccountOperationException failure = Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(new ArticleContent(), true));
        Assert.Equal(new[] { "body", "categoryId", "summary", "title" }, failure.Errors!.Keys.Order(StringComparer.Ordinal));
        ArticleContent complete = Content("Report body") with { Title = "Report", Summary = "Summary", CategoryId = Guid.NewGuid() };
        failure = Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(complete with { CategoryId = null }, true));
        Assert.Equal("categoryId", Assert.Single(failure.Errors!).Key);
        failure = Assert.Throws<AccountOperationException>(() => ArticleDocuments.Validate(complete with { UsesOthers = true }, true));
        Assert.Equal("sources", Assert.Single(failure.Errors!).Key);
    }

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
