using CityPortal.Identity;

namespace CityPortal.IntegrationTests;

public sealed class AccountValidationTests
{
    [Theory]
    [InlineData("abc", "!!!!!!!!!")]
    [InlineData("\u0410\u043b\u0435\u043a\u0441\u0435\u0439123", "aB12345!")]
    [InlineData("\U00010400\U00010401\U00010402", "!!!!!!!!")]
    [InlineData("12345678901234567890", "12345678901234567890123456789!")]
    public void Registration_WithAllowedNameAndPassword_AcceptsExactValues(string name, string password)
    {
        (string email, string displayName) = AccountValidation.ValidateRegistration(" Test@example.test ", " " + name + " ", password);
        Assert.Equal("Test@example.test", email);
        Assert.Equal(name, displayName);
    }

    [Fact]
    public void Registration_WithDecomposedLetters_NormalizesBeforeScalarLengthValidation()
    {
        (_, string name) = AccountValidation.ValidateRegistration("test@example.test", "e\u0301e\u0301e\u0301", "1234567!");
        Assert.Equal("\u00e9\u00e9\u00e9", name);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("123456789012345678901")]
    [InlineData("a b")]
    [InlineData("a-b")]
    [InlineData("a_b")]
    [InlineData("ab\u00b2")]
    [InlineData("abc\U0001f600")]
    public void Registration_WithInvalidName_ReportsDisplayNameError(string name)
    {
        AccountOperationException error = Assert.Throws<AccountOperationException>(() => AccountValidation.ValidateRegistration("test@example.test", name, "1234567!"));
        Assert.Equal(400, error.Status);
        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(["displayName"], error.Errors!.Keys);
    }

    [Theory]
    [InlineData("123456!")]
    [InlineData("123456789012345678901234567890!")]
    [InlineData("12345678")]
    [InlineData("123456! ")]
    [InlineData("\u041f\u0430\u0440\u043e\u043b\u044c123!")]
    [InlineData("123456!\t")]
    public void Registration_WithInvalidPassword_ReportsPasswordError(string password)
    {
        AccountOperationException error = Assert.Throws<AccountOperationException>(() => AccountValidation.ValidateRegistration("test@example.test", "Alice", password));
        Assert.Equal(["password"], error.Errors!.Keys);
    }

    [Theory]
    [InlineData(254, true)]
    [InlineData(255, false)]
    public void Registration_EmailLengthBoundary_IsEnforced(int length, bool accepted)
    {
        string email = new('a', length - "@example.test".Length);
        email += "@example.test";
        if (accepted)
            Assert.Equal(email, AccountValidation.ValidateRegistration(email, "Alice", "1234567!").Email);
        else
            Assert.Contains("email", Assert.Throws<AccountOperationException>(() => AccountValidation.ValidateRegistration(email, "Alice", "1234567!")).Errors!.Keys);
    }
}
