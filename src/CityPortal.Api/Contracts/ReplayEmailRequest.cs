namespace CityPortal.Api.Contracts;

/// <summary>Expected version of a still-valid failed Email delivery.</summary>
public sealed record ReplayEmailRequest
{
    public long Version { get; init; }
}
