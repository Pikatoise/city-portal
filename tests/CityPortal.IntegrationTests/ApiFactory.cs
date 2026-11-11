using CityPortal.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CityPortal.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly string _database;
    private readonly string _messaging;

    public ApiFactory(string environment, string database, string messaging)
    {
        _environment = environment;
        _database = database;
        _messaging = messaging;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:Database", _database);
        builder.UseSetting("ConnectionStrings:Messaging", _messaging);
    }
}
