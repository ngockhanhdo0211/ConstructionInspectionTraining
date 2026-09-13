using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ConstructionInspection.IntegrationTests.Authentication;

public sealed class AuthEndpointsTests : IClassFixture<AuthApiFactory>
{
    private readonly AuthApiFactory _factory;

    public AuthEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_ThenMe_ReturnsAuthenticatedUser()
    {
        using var client = CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = AuthApiFactory.Email, password = AuthApiFactory.Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(AuthApiFactory.UserId, payload.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal("INSPECTOR", payload.GetProperty("user").GetProperty("roles")[0].GetString());
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("ConstructionInspection.Tests", jwt.Issuer);
        Assert.Contains("ConstructionInspection.Tests.Client", jwt.Audiences);
        Assert.NotEqual(DateTime.MinValue, jwt.ValidTo);
        Assert.Equal(AuthApiFactory.UserId.ToString(), jwt.Subject);
        Assert.Contains(jwt.Claims, claim => claim.Type == "email");
        Assert.Contains(jwt.Claims, claim => claim.Type == "name");
        Assert.Contains(jwt.Claims, claim => claim.Type == "role" && claim.Value == "INSPECTOR");
        Assert.Contains(jwt.Claims, claim => claim.Type == "jti");
        Assert.Contains(jwt.Claims, claim => claim.Type == "iat");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var currentUser = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AuthApiFactory.UserId, currentUser.GetProperty("id").GetGuid());
        Assert.Equal(AuthApiFactory.Email, currentUser.GetProperty("email").GetString());
        Assert.Equal("INSPECTOR", currentUser.GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = AuthApiFactory.Email, password = "invalid-test-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithInvalidRequest_ReturnsValidationProblem()
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "invalid", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_WithModifiedSignature_ReturnsUnauthorized()
    {
        using var client = CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = AuthApiFactory.Email, password = AuthApiFactory.Password });
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("accessToken").GetString()!;
        var replacement = token[^1] == 'A' ? 'B' : 'A';
        var modifiedToken = token[..^1] + replacement;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", modifiedToken);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });
}

public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public const string Email = "inspector@example.test";
    public static readonly string Password = Guid.NewGuid().ToString("N");
    private static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Issuer", "ConstructionInspection.Tests");
        builder.UseSetting("Jwt:Audience", "ConstructionInspection.Tests.Client");
        builder.UseSetting(
            "Jwt:SigningKey",
            SigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "5");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityService>();
            services.AddSingleton<IIdentityService>(new TestIdentityService());
        });
    }

    private sealed class TestIdentityService : IIdentityService
    {
        private static readonly UserProfile User = new(
            UserId,
            Email,
            "Test Inspector",
            ["INSPECTOR"]);

        public Task<UserProfile?> AuthenticateAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            var result = string.Equals(email, Email, StringComparison.OrdinalIgnoreCase)
                && password == Password
                    ? User
                    : null;
            return Task.FromResult<UserProfile?>(result);
        }

        public Task<UserProfile?> GetUserAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<UserProfile?>(userId == UserId ? User : null);
    }
}
