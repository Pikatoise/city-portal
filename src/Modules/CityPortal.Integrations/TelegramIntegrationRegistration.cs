using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Wolverine;
using Wolverine.RabbitMQ;

namespace CityPortal.Integrations;

public static class TelegramIntegrationRegistration
{
    public static IServiceCollection AddTelegramIntegrations(this IServiceCollection services, IConfiguration configuration, bool isDevelopment = false)
    {
        bool useStubs = configuration.GetValue<bool>("Integrations:UseLocalStubs");
        if (useStubs && (!isDevelopment || configuration["Integrations:Telegram:BotToken"] != "1234:local_stub"))
            throw new InvalidOperationException("Local integration stubs require Development and the fixture Telegram token.");

        services.AddScoped<TelegramWorkflow>();
        services.AddScoped<TelegramDeliveryWorker>();
        services.AddScoped<TelegramAnnouncementAi>();
        services.AddHttpClient<TelegramBotClient>(client =>
        {
            client.BaseAddress = new Uri(useStubs ? "http://integration-stubs:8080/" : "https://api.telegram.org/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 65536;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ActivityHeadersPropagator = null })
            .RemoveAllLoggers();

        string? key = configuration["Integrations:OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key))
            services.AddChatClient(_ => (useStubs
                ? new OpenAIClient(new ApiKeyCredential("local-stub"), new OpenAIClientOptions { Endpoint = new Uri("http://integration-stubs:8080/v1") })
                : new OpenAIClient(key)).GetChatClient("gpt-4.1-mini-2025-04-14").AsIChatClient());
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
