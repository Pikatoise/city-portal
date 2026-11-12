using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace CityPortal.Identity;

public static class AccountValidation
{
    public static (string Email, string DisplayName) ValidateRegistration(string? email, string? displayName, string? password)
    {
        string cleanEmail = email?.Trim() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (cleanEmail.Length > 254 || !MailAddress.TryCreate(cleanEmail, out MailAddress? address) || address.Address != cleanEmail || !cleanEmail.Contains('@'))
            errors["email"] = ["Enter a valid Email address of at most 254 characters."];
        string cleanName = ValidateName(displayName, errors);
        ValidatePassword(password, "password", errors);
        if (errors.Count > 0)
            throw new AccountOperationException(400, "validation_failed", "Some fields are invalid.", errors);
        return (cleanEmail, cleanName);
    }

    public static string ValidateDisplayName(string? displayName)
    {
        var errors = new Dictionary<string, string[]>();
        string name = ValidateName(displayName, errors);
        if (errors.Count > 0)
            throw new AccountOperationException(400, "validation_failed", "Some fields are invalid.", errors);
        return name;
    }

    public static string ValidateNewPassword(string? password)
    {
        var errors = new Dictionary<string, string[]>();
        ValidatePassword(password, "newPassword", errors);
        if (errors.Count > 0)
            throw new AccountOperationException(400, "validation_failed", "Some fields are invalid.", errors);
        return password ?? throw new InvalidOperationException("Validated password is required.");
    }

    private static string ValidateName(string? displayName, Dictionary<string, string[]> errors)
    {
        string name = (displayName?.Trim() ?? string.Empty).Normalize(NormalizationForm.FormC);
        Rune[] characters = name.EnumerateRunes().ToArray();
        if (characters.Length is < 3 or > 20 || characters.Any(character => !Rune.IsLetter(character) && Rune.GetUnicodeCategory(character) != UnicodeCategory.DecimalDigitNumber))
            errors["displayName"] = ["Use 3-20 Unicode letters or decimal digits."];
        return name;
    }

    private static void ValidatePassword(string? password, string field, Dictionary<string, string[]> errors)
    {
        if (password is null || password.Length is < 8 or > 30 || password.Any(character => character is < '!' or > '~') || !password.Any(character => !char.IsAsciiLetterOrDigit(character)))
            errors[field] = ["Use 8-30 printable ASCII characters without spaces, including punctuation."];
    }
}
