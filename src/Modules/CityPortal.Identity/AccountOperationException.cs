namespace CityPortal.Identity;

public sealed class AccountOperationException : Exception
{
    public AccountOperationException(int status, string code, string message, Dictionary<string, string[]>? errors = null, int retryAfterSeconds = 0) : base(message)
    {
        Status = status;
        Code = code;
        Errors = errors;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public int Status { get; }

    public string Code { get; }

    public Dictionary<string, string[]>? Errors { get; }

    public int RetryAfterSeconds { get; }
}
