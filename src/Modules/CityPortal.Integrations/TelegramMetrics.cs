using System.Diagnostics.Metrics;

namespace CityPortal.Integrations;

internal static class TelegramMetrics
{
    private static readonly Meter _meter = new("CityPortal.Telegram");
    internal static Counter<long> Dispatches { get; } = _meter.CreateCounter<long>("cityportal.telegram.dispatches");
    internal static Counter<long> Outcomes { get; } = _meter.CreateCounter<long>("cityportal.telegram.outcomes");
    internal static Counter<long> AiOutcomes { get; } = _meter.CreateCounter<long>("cityportal.telegram.ai.outcomes");
}
