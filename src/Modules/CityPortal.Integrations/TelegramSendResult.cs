namespace CityPortal.Integrations;

internal sealed record TelegramSendResult(int? MessageId, int? RetryAfterSeconds, string? ErrorCode, bool OutcomeUnknown);
