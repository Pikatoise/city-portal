using System.Text;
using CityPortal.Identity;

namespace CityPortal.Media;

internal static class MediaText
{
    public static string Normalize(string text, int maximum, string code)
    {
        string normalized;
        try
        {
            normalized = text.Trim().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            throw new AccountOperationException(400, code, "Provide valid Unicode text.");
        }
        if (normalized.EnumerateRunes().Count() > maximum)
            throw new AccountOperationException(400, code, $"Provide at most {maximum} characters.");
        return normalized;
    }
}
