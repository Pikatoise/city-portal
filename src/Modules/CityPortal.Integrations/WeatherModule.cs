using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace CityPortal.Integrations;

public static class WeatherModule
{
    public static IServiceCollection AddWeatherWorker(this IServiceCollection services)
    {
        services.AddHttpClient<OpenMeteoClient>(http =>
        {
            http.BaseAddress = new Uri("https://api.open-meteo.com/");
            http.Timeout = TimeSpan.FromSeconds(5);
        });
        return services;
    }

    public static void ConfigureWeatherJobs(IQuartzBuilder quartz)
    {
        var key = new JobKey("refresh-current-weather", "integrations");
        quartz.AddJob<WeatherRefreshJob>(job => job.WithIdentity(key).RequestRecovery());
        quartz.AddTrigger(trigger => trigger.WithIdentity("current-weather-interval", key.Group).ForJob(key).StartNow()
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMinutes(30)).RepeatForever()
                .WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)));
    }
}
