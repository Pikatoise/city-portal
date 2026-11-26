using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CityPortal.Api;
using CityPortal.Integrations;
using CityPortal.News;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CityPortal.IntegrationTests;

public sealed partial class NewsTests
{
    private const string _mcpPath = "/api/mcp";
    private const string _mcpOrigin = "https://city.test";
    private const string _mcpResource = _mcpOrigin + _mcpPath;
    private const string _mcpIssuer = "https://auth.city.test/";
    private const string _mcpMetadata = "/.well-known/oauth-protected-resource" + _mcpPath;

    [Fact]
    public async Task Mcp_UsesAuth0MetadataAndRejectsTokensWithoutClientAudienceOrScopeAsync()
    {
        (ApiFactory factory, _, _) = await _fixture.CreateAsync(McpSettings());
        await using ApiFactory lifetime = factory;
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "mcp-test" };
        await using WebApplicationFactory<Program> configured = ConfigureMcpTokens(factory, key);
        using HttpClient client = configured.CreateClient(new() { BaseAddress = new Uri(_mcpOrigin) });
        using HttpResponseMessage disabled = await client.GetAsync(_mcpMetadata);
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        await SetMcpEnabledAsync(factory);

        JsonElement metadata = await client.GetFromJsonAsync<JsonElement>(_mcpMetadata);
        Assert.Equal(_mcpResource, metadata.GetProperty("resource").GetString());
        Assert.Equal(_mcpIssuer, Assert.Single(metadata.GetProperty("authorization_servers").EnumerateArray()).GetString());
        Assert.Equal("news:read", Assert.Single(metadata.GetProperty("scopes_supported").EnumerateArray()).GetString());

        using HttpResponseMessage unauthorized = await SendMcpAsync(client, "tools/list");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Contains(_mcpOrigin + _mcpMetadata, Assert.Single(unauthorized.Headers.WwwAuthenticate).Parameter);

        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = key.KeyId };
        foreach (string invalidToken in new[]
        {
            CreateMcpToken(key, audience: "https://other.test/api/mcp"),
            CreateMcpToken(key, audience: _mcpResource + "/"),
            CreateMcpToken(key, issuer: "https://other.test/"),
            CreateMcpToken(key, expired: true),
            CreateMcpToken(otherKey)
        })
        {
            using HttpResponseMessage rejected = await SendMcpAsync(client, "tools/list", invalidToken);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Contains("invalid_token", Assert.Single(rejected.Headers.WwwAuthenticate).Parameter);
        }
        foreach (string unauthorizedToken in new[]
        {
            CreateMcpToken(key, clientId: "unregistered"),
            CreateMcpToken(key, scope: "openid news:write"),
            CreateMcpToken(key, scope: "other:news:read")
        })
        {
            using HttpResponseMessage forbidden = await SendMcpAsync(client, "tools/list", unauthorizedToken);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        string token = CreateMcpToken(key, scope: "openid news:read");
        JsonElement initialized = await McpResultAsync(client, "initialize", token, new
        {
            protocolVersion = "2025-11-25",
            capabilities = new { },
            clientInfo = new { name = "CityPortal test", version = "1.0" }
        });
        Assert.Equal("2025-11-25", initialized.GetProperty("protocolVersion").GetString());
        JsonElement list = await McpResultAsync(client, "tools/list", token);
        JsonElement[] tools = list.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Equal(new[] { "get_article", "list_latest_news", "search_news" }, tools.Select(tool => tool.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.True(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));
        await SetMcpEnabledAsync(factory, enabled: false);
        using HttpResponseMessage disabledWithToken = await SendMcpAsync(client, "tools/list", token);
        Assert.Equal(HttpStatusCode.NotFound, disabledWithToken.StatusCode);
    }

    [Fact]
    public async Task Mcp_RejectsCrossOriginAndOversizedRequestsAndLimitsRequestsPerIpAsync()
    {
        (ApiFactory factory, _, _) = await _fixture.CreateAsync(McpSettings());
        await using ApiFactory lifetime = factory;
        await SetMcpEnabledAsync(factory);
        using HttpClient client = factory.CreateClient(new() { BaseAddress = new Uri(_mcpOrigin) });
        using HttpResponseMessage forbidden = await SendMcpAsync(client, "tools/list", origin: "https://elsewhere.test");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var content = new StringContent(new string('x', 16 * 1024 + 1), Encoding.UTF8, "application/json");
        using HttpResponseMessage oversized = await client.PostAsync(_mcpPath, content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        for (int i = 1; i < 60; i++)
        {
            using HttpResponseMessage admitted = await client.GetAsync(_mcpMetadata);
            Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        }
        using HttpResponseMessage limited = await client.GetAsync(_mcpMetadata);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Mcp_ListSearchAndGetFollowTheCurrentPublicRevisionAsync()
    {
        (ApiFactory factory, string database, _) = await _fixture.CreateAsync(McpSettings());
        await using ApiFactory lifetime = factory;
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "mcp-test" };
        await using WebApplicationFactory<Program> configured = ConfigureMcpTokens(factory, key);
        using HttpClient client = configured.CreateClient(new() { BaseAddress = new Uri(_mcpOrigin) });
        string token = CreateMcpToken(key);
        await SetMcpEnabledAsync(factory);
        (_, HttpClient owner) = await ActorAsync(factory, database, "Author");
        (_, HttpClient editor) = await ActorAsync(factory, database, "Editor");
        using (owner)
        using (editor)
        {
            using HttpResponseMessage fromCookie = await SendMcpAsync(editor, "tools/list");
            Assert.Equal(HttpStatusCode.Unauthorized, fromCookie.StatusCode);
            TaxonomyResponse category = await TaxonomyAsync(editor, "categories", "City");
            ArticleContent first = NewsDocumentTests.Content("Library opens on Friday.") with
            {
                Title = "Library opening",
                Summary = "Public summary",
                CategoryId = category.Id
            };
            await SaveAsync(owner, await CreateAsync(owner), first with { Title = "Unpublished archive" });
            ArticleResponse article = await PublishDiscoveryAsync(owner, editor, first);
            article = await ActAsync(owner, article, "working-version");
            article = await SaveAsync(owner, article, first with { Title = "Private draft", Summary = "Private summary" });

            JsonElement listed = Assert.Single((await McpToolAsync(client, token, "list_latest_news")).GetProperty("structuredContent").GetProperty("items").EnumerateArray());
            string address = Assert.IsType<string>(listed.GetProperty("address").GetString());
            Assert.Equal(first.Title, listed.GetProperty("title").GetString());
            Assert.Equal(_mcpOrigin + "/news/" + address, listed.GetProperty("url").GetString());
            JsonElement searched = Assert.Single((await McpToolAsync(client, token, "search_news", new { query = "Library" })).GetProperty("structuredContent").GetProperty("items").EnumerateArray());
            Assert.Equal(first.Summary, searched.GetProperty("summary").GetString());
            Assert.Empty((await McpToolAsync(client, token, "search_news", new { query = "Private draft" })).GetProperty("structuredContent").GetProperty("items").EnumerateArray());
            JsonElement published = (await McpToolAsync(client, token, "get_article", new { address })).GetProperty("structuredContent");
            Assert.Equal("Library opens on Friday.", published.GetProperty("text").GetString());
            Assert.Equal(first.Summary, published.GetProperty("summary").GetString());

            ArticleContent correction = NewsDocumentTests.Content("Library opens on Saturday.") with { Title = first.Title, Summary = first.Summary, CategoryId = category.Id };
            article = await CorrectAsync(editor, article, correction, "Update date");
            JsonElement corrected = (await McpToolAsync(client, token, "get_article", new { address })).GetProperty("structuredContent");
            Assert.Equal("Library opens on Saturday.", corrected.GetProperty("text").GetString());
            article = await ActAsync(editor, article, "withdraw", "Verification needed");
            Assert.True((await McpToolAsync(client, token, "get_article", new { address })).GetProperty("isError").GetBoolean());
            Assert.Empty((await McpToolAsync(client, token, "list_latest_news")).GetProperty("structuredContent").GetProperty("items").EnumerateArray());
            article = await SaveAsync(owner, article, first);
            article = await ActAsync(owner, article, "submit", "Verified original date");
            article = await ActAsync(editor, article, "approve");
            await ActAsync(owner, article, "publish");
            JsonElement republished = (await McpToolAsync(client, token, "get_article", new { address })).GetProperty("structuredContent");
            Assert.Equal("Library opens on Friday.", republished.GetProperty("text").GetString());
        }
    }

    private static Dictionary<string, string> McpSettings() => new()
    {
        ["PublicOrigin"] = _mcpOrigin,
        ["Mcp:Authority"] = _mcpIssuer,
        ["Mcp:AllowedClientIds:0"] = "inspector"
    };

    private static async Task SetMcpEnabledAsync(ApiFactory factory, bool enabled = true)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IntegrationDbContext database = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
        (await database.Settings.SingleAsync()).McpEnabled = enabled;
        await database.SaveChangesAsync();
    }

    private static WebApplicationFactory<Program> ConfigureMcpTokens(ApiFactory factory, SecurityKey key) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>("NewsMcpBearer", options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = _mcpIssuer };
            configuration.SigningKeys.Add(key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        })));

    private static string CreateMcpToken(SecurityKey key, string audience = _mcpResource, string clientId = "inspector",
        string scope = "news:read", string issuer = _mcpIssuer, bool expired = false)
    {
        DateTime now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = now.AddMinutes(-10),
            IssuedAt = now.AddMinutes(-10),
            Expires = now.AddMinutes(expired ? -5 : 5),
            Subject = new ClaimsIdentity([new Claim("sub", "auth0|reader"), new Claim("azp", clientId), new Claim("scope", scope)]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        });
    }

    private static async Task<HttpResponseMessage> SendMcpAsync(HttpClient client, string method, string? token = null, object? arguments = null, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _mcpPath)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = arguments ?? new { } })
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    private static Task<JsonElement> McpToolAsync(HttpClient client, string token, string name, object? arguments = null) =>
        McpResultAsync(client, "tools/call", token, new { name, arguments = arguments ?? new { } });

    private static async Task<JsonElement> McpResultAsync(HttpClient client, string method, string token, object? arguments = null)
    {
        using HttpResponseMessage response = await SendMcpAsync(client, method, token, arguments);
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            body = Assert.Single(body.Split('\n'), line => line.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("result", out JsonElement result), body);
        return result.Clone();
    }
}
