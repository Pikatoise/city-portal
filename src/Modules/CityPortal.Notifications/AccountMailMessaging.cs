using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace CityPortal.Notifications;

public static class AccountMailMessaging
{
    public static IHostBuilder UseAccountMailMessaging(this IHostBuilder host, string database, string messaging, bool isWorker)
    {
        return host.UseWolverine(options =>
        {
            options.ServiceName = isWorker ? "CityPortal.Worker" : "CityPortal.Api";
            options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
            // Readiness exposes unavailable storage; liveness remains reachable during outages and before migration.
            options.ResourceMigrationFailureMode = ResourceMigrationFailureMode.ContinueOnFailures;
            options.BrokerInitializationTimeout = TimeSpan.FromSeconds(2);
            options.PersistMessagesWithPostgresql(database, "notifications_messaging");
            options.UseEntityFrameworkCoreTransactions();
            options.UseRabbitMq(new Uri(messaging)).ConfigureConnection(factory => factory.RequestedConnectionTimeout = TimeSpan.FromSeconds(2)).AutoProvision();
            options.PublishMessage<DeliverAccountMail>().ToRabbitQueue("cityportal.account-mail").UseDurableOutbox();
            options.Policies.UseDurableLocalQueues();
            options.OnException<Exception>().RetryWithCooldown(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));

            if (isWorker)
            {
                // The module keeps its DbContext internal and constructs this scoped operation at its boundary.
                options.CodeGeneration.AlwaysUseServiceLocationFor<AccountMailDeliveryService>();
                options.Discovery.IncludeAssembly(typeof(DeliverAccountMailHandler).Assembly);
                options.ListenToRabbitQueue("cityportal.account-mail").UseDurableInbox();
            }
            else
            {
                options.Durability.Mode = DurabilityMode.ServerlessWithOutbox;
                options.Discovery.DisableConventionalDiscovery();
            }
        });
    }

    public static Task MigrateAccountMailMessagingAsync(this IServiceProvider services)
        => services.GetRequiredService<IMessageStore>().Admin.MigrateAsync(AutoCreate.CreateOrUpdate);
}
