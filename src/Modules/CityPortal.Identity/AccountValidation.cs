using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace CityPortal.Identity;

public static class AccountValidation
{
    public static (string Email, string DisplayName) ValidateRegistration(string? email, string? displayName, string? password)
    {
        string cleanEmail = email?.Trim() ?? string.Empty;
        string cleanName = (displayName?.Trim() ?? string.Empty).Normalize(NormalizationForm.FormC);
        var errors = new Dictionary<string, string[]>();
        if (cleanEmail.Length > 254 || !MailAddress.TryCreate(cleanEmail, out MailAddress? address) || address.Address != cleanEmail || !cleanEmail.Contains('@'))
            errors["email"] = ["Enter a valid Email address of at most 254 characters."];
        Rune[] characters = cleanName.EnumerateRunes().ToArray();
        if (characters.Length is < 3 or > 20 || characters.Any(character => !Rune.IsLetter(character) && Rune.GetUnicodeCategory(character) != UnicodeCategory.DecimalDigitNumber))
            errors["displayName"] = ["Use 3-20 Unicode letters or decimal digits."];
        if (password is null || password.Length is < 8 or > 30 || password.Any(character => character is < '!' or > '~') || !password.Any(character => !char.IsAsciiLetterOrDigit(character)))
            errors["password"] = ["Use 8-30 printable ASCII characters without spaces, including punctuation."];
        if (errors.Count > 0)
            throw new AccountOperationException(400, "validation_failed", "Some fields are invalid.", errors);
        return (cleanEmail, cleanName);
    }
}
