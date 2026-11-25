namespace CityPortal.Reports;

internal static class ReportCategories
{
    internal static bool Contains(string kind, string category) =>
        category is "spam" or "fraud" or "personal-data" or "insults" or "other"
        || (kind == "articles"
            ? category is "inaccurate-information" or "misleading-headline" or "copyright" or "hate-speech" or "inappropriate-media"
            : category is "prohibited-item" or "wrong-category" or "inaccurate-description" or "incorrect-price" or "inappropriate-photos");
}
