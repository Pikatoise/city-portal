namespace CityPortal.Api.Contracts;

/// <summary>Returns an editable announcement generated from currently published text.</summary>
public sealed record TelegramSuggestionResponse(string Text);
