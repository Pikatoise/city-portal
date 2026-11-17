using System.Globalization;
using System.Text;

namespace CityPortal.News;

/// <summary>Canonical public addresses shared by article and discovery reads.</summary>
public static class ArticleUrls
{
    private static readonly string[] _russian = [
        "a", "b", "v", "g", "d", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p",
        "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya"
    ];

    public static string Address(string title, DateOnly firstPublishedDate, Guid id)
    {
        var builder = new StringBuilder();
        bool separator = false;
        foreach (Rune rune in title.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            var lower = Rune.ToLowerInvariant(rune);
            int value = lower.Value;
            string part = value == 0x451 ? "yo" : value is >= 0x430 and <= 0x44f ? _russian[value - 0x430]
                : value is >= 'a' and <= 'z' or >= '0' and <= '9' ? lower.ToString() : "";
            if (part.Length == 0)
            {
                if (value is not (0x44a or 0x44c))
                    separator = true;
                continue;
            }
            int needed = part.Length + (separator && builder.Length > 0 ? 1 : 0);
            if (builder.Length + needed > 120)
                break;
            if (separator && builder.Length > 0)
                builder.Append('-');
            builder.Append(part);
            separator = false;
        }
        string slug = builder.Length == 0 ? "article" : builder.ToString();
        return $"/news/{slug}-{firstPublishedDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}-{id.ToString("N")[^6..]}";
    }

}
