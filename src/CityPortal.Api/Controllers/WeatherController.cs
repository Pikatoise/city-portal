using CityPortal.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CityPortal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Produces("application/json")]
[Route("api/weather")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WeatherController : ControllerBase
{
    private readonly PublicIntegrationReads _integrations;

    public WeatherController(PublicIntegrationReads integrations) => _integrations = integrations;

    /// <summary>Returns the latest usable city weather reading.</summary>
    [HttpGet("current", Name = "getCurrentWeather")]
    [ProducesResponseType<CurrentWeatherResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<CurrentWeatherResponse>> GetAsync(CancellationToken cancellationToken)
    {
        CurrentWeatherResponse? current = await _integrations.CurrentWeatherAsync(cancellationToken);
        return current is null ? NoContent() : Ok(current);
    }
}
