namespace CityPortal.Reports;

internal static class ReportCategories
{
    internal static bool Contains(string kind, string category) =>
        kind == "comments" ? category is "spam" or "insults" or "hate-speech" or "personal-data" or "other"
        : category is "spam" or "fraud" or "personal-data" or "insults" or "other"
        || (kind == "articles"
            ? category is "inaccurate-information" or "misleading-headline" or "copyright" or "hate-speech" or "inappropriate-media"
            : category is "prohibited-item" or "wrong-category" or "inaccurate-description" or "incorrect-price" or "inappropriate-photos");
}
