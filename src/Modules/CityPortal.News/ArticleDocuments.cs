using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CityPortal.Identity;

namespace CityPortal.News;

public static class ArticleDocuments
{
    public static ValidatedArticleContent Validate(ArticleContent content, bool requireComplete = false)
    {
        if (content.SchemaVersion != 1 || content.Body.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(content.Body.GetRawText()) > 2 * 1024 * 1024)
            throw NewsDatabase.Invalid("body", "Use a version 1 structured document of up to 2 MiB.");
        if (content.TagIds is null || content.Sources is null || content.Gallery is null || content.TagIds.Length > 10 || content.Sources.Length > 20)
            throw NewsDatabase.Invalid("content", "Use at most ten tags and twenty sources.");
        var document = new DocumentValidation();
        JsonObject body = document.Node(content.Body, "root", 0);
        if (document.Characters > 100000)
            throw NewsDatabase.Invalid("body", "The body must not exceed 100000 plain-text characters.");
        ArticleImage? cover = content.Cover is null ? null : Image(content.Cover, requireComplete);
        ArticleImage[] gallery = content.Gallery.Select(image => Image(image, requireComplete)).ToArray();
        Guid[] ids = document.Images.Concat(cover is null ? [] : new[] { cover.MediaId }).Concat(gallery.Select(image => image.MediaId)).Distinct().ToArray();
        if (ids.Length > 20 || content.TagIds.Contains(Guid.Empty) || content.CategoryId == Guid.Empty)
            throw NewsDatabase.Invalid("images", "Use at most twenty distinct images and valid taxonomy IDs.");
        ArticleSource[] sources = content.Sources.Select(source => source is null
            ? throw NewsDatabase.Invalid("sources", "Provide valid source entries.")
            : new ArticleSource(Text(source.Name, 100, "sources"), Url(source.Url, "sources"))).ToArray();
        ArticleContent normalized = content with
        {
            Title = Text(content.Title, 200, "title"),
            Summary = Text(content.Summary, 500, "summary"),
            SeoTitle = Text(content.SeoTitle, 200, "seoTitle"),
            SeoDescription = Text(content.SeoDescription, 500, "seoDescription"),
            TagIds = content.TagIds.Distinct().ToArray(),
            Body = JsonSerializer.SerializeToElement(body),
            Cover = cover,
            Gallery = gallery,
            Sources = sources
        };
        if (requireComplete)
        {
            var errors = new Dictionary<string, string[]>();
            if (normalized.Title.Length == 0)
                errors["title"] = ["Provide a title before submission."];
            if (normalized.Summary.Length == 0)
                errors["summary"] = ["Provide a summary before submission."];
            if (normalized.CategoryId is null)
                errors["categoryId"] = ["Choose a category before submission."];
            if (string.IsNullOrWhiteSpace(document.PlainText.ToString()))
                errors["body"] = ["Provide article text before submission."];
            if (normalized.UsesOthers && sources.Length == 0 || sources.Any(source => source.Name.Length == 0 || source.Url.Length == 0))
                errors["sources"] = ["Provide a name and URL for each source; attribution requires at least one source."];
            if (document.MissingAlt)
                errors["bodyAlt"] = ["Every image in the article body requires alternative text before submission."];
            if (errors.Count > 0)
                throw new AccountOperationException(400, "invalid_article", "Complete the indicated fields before submission.", errors);
        }
        return new ValidatedArticleContent(normalized, document.Html.ToString(), document.PlainText.ToString(), ids);
    }

    internal static string Text(string? value, int maximum, string field, bool trim = true)
    {
        string normalized;
        try
        {
            normalized = (trim ? value?.Trim() : value)?.Normalize(NormalizationForm.FormC) ?? "";
        }
        catch (ArgumentException)
        {
            throw NewsDatabase.Invalid(field, "Provide valid Unicode text.");
        }
        if (normalized.EnumerateRunes().Count() > maximum || normalized.Contains('\0'))
            throw NewsDatabase.Invalid(field, $"Use at most {maximum} characters without null characters.");
        return normalized;
    }

    private static ArticleImage Image(ArticleImage? image, bool required)
    {
        if (image is null || image.MediaId == Guid.Empty)
            throw NewsDatabase.Invalid("images", "Provide a valid Media ID.");
        string alt = Text(image.Alt, 500, "alt");
        if (required && alt.Length == 0)
            throw NewsDatabase.Invalid("alt", "Every image placement requires alternative text at submission.");
        return image with { Alt = alt, Caption = Text(image.Caption, 1000, "caption") };
    }

    private static string Url(string? value, string field)
    {
        string url = Text(value, 2048, field);
        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) || parsed.Scheme is not ("http" or "https") || string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo)
            || url.Any(char.IsControl)))
            throw NewsDatabase.Invalid(field, "Use absolute HTTP or HTTPS links without embedded credentials.");
        return url;
    }

    private sealed class DocumentValidation
    {
        public StringBuilder Html { get; } = new();
        public StringBuilder PlainText { get; } = new();
        public List<Guid> Images { get; } = [];
        public int Characters { get; private set; }
        public bool MissingAlt { get; private set; }
        private int _blocks;
        private int _nodes;

        public JsonObject Node(JsonElement element, string parent, int depth)
        {
            if (depth > 24 || ++_nodes > 20000 || element.ValueKind != JsonValueKind.Object)
                throw NewsDatabase.Invalid("body", "The document is too deeply nested or contains too many nodes.");
            Only(element, "type", "attrs", "text", "marks", "content");
            string type = String(element, "type");
            bool block = type is "paragraph" or "heading" or "bulletList" or "orderedList" or "blockquote" or "mediaFigure";
            bool allowed = parent switch
            {
                "root" => type == "doc",
                "doc" or "blockquote" => block,
                "listItem" => block,
                "bulletList" or "orderedList" => type == "listItem",
                "paragraph" or "heading" => type is "text" or "hardBreak",
                _ => false
            };
            if (!allowed || block && ++_blocks > 2000)
                throw NewsDatabase.Invalid("body", "Use only the supported document blocks and nesting (up to 2000 blocks).");
            var result = new JsonObject { ["type"] = type };
            if (type is "text" or "hardBreak")
            {
                bool lineBreak = type == "hardBreak";
                if (lineBreak)
                {
                    Only(Attributes(element));
                    RejectChildren(element);
                    if (element.TryGetProperty("text", out _))
                        throw NewsDatabase.Invalid("body", "Only text nodes may contain text.");
                }
                else if (element.TryGetProperty("attrs", out _) || element.TryGetProperty("content", out _))
                    throw NewsDatabase.Invalid("body", "Text nodes cannot contain attributes or children.");
                string text = lineBreak ? "\n" : Text(String(element, "text"), 100000, "body", trim: false);
                if (text.Length == 0)
                    throw NewsDatabase.Invalid("body", "Text nodes must not be empty.");
                Characters += text.EnumerateRunes().Count();
                PlainText.Append(text);
                string rendered = lineBreak ? "<br>" : HtmlEncoder.Default.Encode(text);
                if (element.TryGetProperty("marks", out JsonElement marks))
                {
                    if (marks.ValueKind != JsonValueKind.Array || marks.GetArrayLength() > 3)
                        throw NewsDatabase.Invalid("body", "Use supported text marks only.");
                    var canonical = new JsonArray();
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonElement mark in marks.EnumerateArray())
                    {
                        Only(mark, "type", "attrs");
                        string name = String(mark, "type");
                        if (!seen.Add(name) || name is not ("bold" or "italic" or "link"))
                            throw NewsDatabase.Invalid("body", "Use bold, italic and HTTP/HTTPS links only.");
                        var canonicalMark = new JsonObject { ["type"] = name };
                        if (name == "link")
                        {
                            JsonElement attributes = Attributes(mark);
                            Only(attributes, "href", "target", "rel", "class");
                            string href = Url(String(attributes, "href"), "link");
                            if (href.Length == 0)
                                throw NewsDatabase.Invalid("link", "Provide an absolute HTTP/HTTPS link.");
                            canonicalMark["attrs"] = new JsonObject { ["href"] = href };
                            rendered = $"<a href=\"{HtmlEncoder.Default.Encode(href)}\" rel=\"noopener noreferrer\">{rendered}</a>";
                        }
                        else
                        {
                            if (mark.TryGetProperty("attrs", out JsonElement attrs))
                                Only(attrs);
                            rendered = name == "bold" ? $"<strong>{rendered}</strong>" : $"<em>{rendered}</em>";
                        }
                        canonical.Add(canonicalMark);
                    }
                    result["marks"] = canonical;
                }
                if (!lineBreak)
                    result["text"] = text;
                Html.Append(rendered);
                return result;
            }
            if (element.TryGetProperty("marks", out _) || element.TryGetProperty("text", out _))
                throw NewsDatabase.Invalid("body", "Only text nodes may contain text or marks.");
            JsonElement nodeAttrs = Attributes(element);
            if (type == "mediaFigure")
            {
                Only(nodeAttrs, "mediaId", "alt", "caption");
                if (!Guid.TryParse(String(nodeAttrs, "mediaId"), out Guid id))
                    throw NewsDatabase.Invalid("images", "Use a valid Media ID.");
                ArticleImage image = Image(new ArticleImage(id, OptionalString(nodeAttrs, "alt"), OptionalString(nodeAttrs, "caption")), false);
                MissingAlt |= image.Alt.Length == 0;
                Images.Add(id);
                result["attrs"] = new JsonObject { ["mediaId"] = id.ToString("D"), ["alt"] = image.Alt, ["caption"] = image.Caption };
                RejectChildren(element);
                Html.Append($"<figure><img src=\"/api/media/images/{id:D}/1280\" alt=\"{HtmlEncoder.Default.Encode(image.Alt)}\" loading=\"lazy\"><figcaption>{HtmlEncoder.Default.Encode(image.Caption)}</figcaption></figure>");
                return result;
            }
            string tag = type switch { "doc" => "div", "paragraph" => "p", "heading" => "h2", "bulletList" => "ul", "orderedList" => "ol", "listItem" => "li", "blockquote" => "blockquote", "hardBreak" => "br", _ => "" };
            string opening = "<" + tag + ">";
            if (type == "heading")
            {
                Only(nodeAttrs, "level");
                int level = Integer(nodeAttrs, "level", 2);
                if (level is not (2 or 3))
                    throw NewsDatabase.Invalid("body", "Use H2 or H3 subheadings.");
                tag = "h" + level;
                opening = "<" + tag + ">";
                result["attrs"] = new JsonObject { ["level"] = level };
            }
            else if (type == "orderedList")
            {
                Only(nodeAttrs, "start", "type");
                int start = Integer(nodeAttrs, "start", 1);
                if (start is < 1 or > 1000000)
                    throw NewsDatabase.Invalid("body", "Use a valid ordered-list start.");
                result["attrs"] = new JsonObject { ["start"] = start };
                opening = $"<ol start=\"{start}\">";
            }
            else
                Only(nodeAttrs);
            Html.Append(opening);
            var children = new JsonArray();
            if (element.TryGetProperty("content", out JsonElement content))
            {
                if (content.ValueKind != JsonValueKind.Array)
                    throw NewsDatabase.Invalid("body", "Block content must be an array.");
                foreach (JsonElement child in content.EnumerateArray())
                    children.Add(Node(child, type, depth + 1));
            }
            if (type is "listItem" && (children.Count == 0 || children[0]?["type"]?.GetValue<string>() != "paragraph") || type is "bulletList" or "orderedList" or "blockquote" && children.Count == 0)
                throw NewsDatabase.Invalid("body", "Lists and quotations require valid child blocks.");
            result["content"] = children;
            Html.Append("</").Append(tag).Append('>');
            if (type is "paragraph" or "heading")
                PlainText.Append('\n');
            return result;
        }

        private static void RejectChildren(JsonElement node)
        {
            if (node.TryGetProperty("content", out _))
                throw NewsDatabase.Invalid("body", "This node cannot have children.");
        }

        private static JsonElement Attributes(JsonElement element) => element.TryGetProperty("attrs", out JsonElement attributes) ? attributes : JsonSerializer.SerializeToElement(new { });

        private static string OptionalString(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? String(element, name) : "";

        private static string String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw NewsDatabase.Invalid("body", $"Provide a string for {name}.");

        private static int Integer(JsonElement element, string name, int fallback) => element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : throw NewsDatabase.Invalid("body", "Provide an integer attribute.") : fallback;

        private static void Only(JsonElement element, params string[] allowed)
        {
            if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(property => !allowed.Contains(property.Name, StringComparer.Ordinal))
                || element.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != element.EnumerateObject().Count())
                throw NewsDatabase.Invalid("body", "Unsupported document property or attribute.");
        }
    }
}
