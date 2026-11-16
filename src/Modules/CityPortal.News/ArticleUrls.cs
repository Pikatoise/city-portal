using System.Text;

namespace CityPortal.News;

/// <summary>Canonical public addresses shared by article and discovery reads.</summary>
public static class ArticleUrls
{
    public static string Slug(string title)
    {
        var builder = new StringBuilder();
        bool separator = false;
        foreach (Rune rune in title.Normalize(NormalizationForm.FormC).EnumerateRunes())
            if (Rune.IsLetterOrDigit(rune))
            {
                if (separator && builder.Length > 0)
                    builder.Append('-');
                builder.Append(Rune.ToLowerInvariant(rune).ToString());
                separator = false;
            }
            else
                separator = true;
        return builder.Length == 0 ? "article" : builder.ToString();
    }

    public static string Canonical(Guid id, string slug) => $"/news/{id}/{Uri.EscapeDataString(slug)}";
}
