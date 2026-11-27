using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CityPortal.Integrations;

public sealed class TelegramBotClient
{
    private readonly HttpClient _http;

    public TelegramBotClient(HttpClient http)
    {
        _http = http;
    }

    internal async Task<TelegramSendResult> SendAsync(string token, string channel, string text, string articleUrl, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsValidToken(token))
                return new TelegramSendResult(null, null, "telegram_rejected", false);
            using HttpResponseMessage response = await _http.PostAsJsonAsync($"./bot{token}/sendMessage",
                new { chat_id = channel, text = $"{text}\n\n{articleUrl}", link_preview_options = new { url = articleUrl } }, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                using JsonDocument? body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                int retryAfter = body is not null && body.RootElement.ValueKind == JsonValueKind.Object
                    && body.RootElement.TryGetProperty("parameters", out JsonElement parameters) && parameters.ValueKind == JsonValueKind.Object
                    && parameters.TryGetProperty("retry_after", out JsonElement delay) && delay.ValueKind == JsonValueKind.Number
                    && delay.TryGetInt32(out int seconds) && seconds > 0 ? seconds : 60;
                return new TelegramSendResult(null, retryAfter, "telegram_rate_limited", false);
            }
            if ((int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.RequestTimeout)
                return new TelegramSendResult(null, null, "telegram_rejected", false);
            if (response.IsSuccessStatusCode)
            {
                using JsonDocument? body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                if (body is not null && body.RootElement.ValueKind == JsonValueKind.Object
                    && body.RootElement.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True
                    && body.RootElement.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.Object
                    && result.TryGetProperty("message_id", out JsonElement id) && id.ValueKind == JsonValueKind.Number
                    && id.TryGetInt32(out int messageId) && messageId > 0)
                    return new TelegramSendResult(messageId, null, null, false);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            // The request URI contains the bot token, so keep only the classified failure.
        }
        return new TelegramSendResult(null, null, "telegram_delivery_unknown", true);
    }

    internal static bool IsValidToken(string? token)
    {
        int separator = token?.IndexOf(':') ?? -1;
        return separator > 0 && token is not null && separator < token.Length - 1
            && token[..separator].All(char.IsAsciiDigit)
            && token[(separator + 1)..].All(value => char.IsAsciiLetterOrDigit(value) || value is '-' or '_');
    }
}
