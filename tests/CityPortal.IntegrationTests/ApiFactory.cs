using CityPortal.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CityPortal.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly string _database;
    private readonly string _messaging;
    private readonly TimeProvider? _time;
    private readonly IReadOnlyDictionary<string, string>? _settings;

    public ApiFactory(string environment, string database, string messaging, TimeProvider? time = null, IReadOnlyDictionary<string, string>? settings = null)
    {
        _environment = environment;
        _database = database;
        _messaging = messaging;
        _time = time;
        _settings = settings;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("Installation:Enabled", "true");
        builder.UseSetting("ConnectionStrings:Database", _database);
        builder.UseSetting("ConnectionStrings:Messaging", _messaging);
        if (_settings is not null)
            foreach (KeyValuePair<string, string> setting in _settings)
                builder.UseSetting(setting.Key, setting.Value);
        if (_time is not null)
            builder.ConfigureServices(services => services.AddSingleton(_time));
    }
}
