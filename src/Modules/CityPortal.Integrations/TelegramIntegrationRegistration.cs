using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Wolverine;
using Wolverine.RabbitMQ;

namespace CityPortal.Integrations;

public static class TelegramIntegrationRegistration
{
    public static IServiceCollection AddTelegramIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TelegramWorkflow>();
        services.AddScoped<TelegramDeliveryWorker>();
        services.AddScoped<TelegramAnnouncementAi>();
        services.AddHttpClient<TelegramBotClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.telegram.org/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 65536;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ActivityHeadersPropagator = null })
            .RemoveAllLoggers();

        string? key = configuration["Integrations:OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key))
            services.AddChatClient(_ => new OpenAIClient(key).GetChatClient("gpt-4.1-mini-2025-04-14").AsIChatClient());
        return services;
    }

    public static void ConfigureTelegramMessaging(this WolverineOptions options, bool isWorker)
    {
        const string queue = "cityportal.telegram";
        options.PublishMessage<TelegramDeliveryMessage>().ToRabbitQueue(queue).UseDurableOutbox();
        if (isWorker)
        {
            options.CodeGeneration.AlwaysUseServiceLocationFor<TelegramDeliveryWorker>();
            options.Discovery.IncludeAssembly(typeof(TelegramDeliveryHandler).Assembly);
            options.ListenToRabbitQueue(queue).UseDurableInbox();
        }
    }
}
