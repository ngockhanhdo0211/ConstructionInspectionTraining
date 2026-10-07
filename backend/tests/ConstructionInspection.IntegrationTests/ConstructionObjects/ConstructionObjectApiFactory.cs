using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionInspection.Infrastructure.Identity;
using ConstructionInspection.Infrastructure.Persistence;
using ConstructionInspection.Application.Drawings.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ConstructionInspection.IntegrationTests.ConstructionObjects;

public sealed class ConstructionObjectApiFactory : WebApplicationFactory<Program>
{
    private const string ExpectedDatabaseName = "ConstructionInspectionIntegrationTestDb";
    private static readonly SemaphoreSlim DatabaseResetLock = new(1, 1);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var values = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: false)
            .Build();
        var connectionString = values.GetConnectionString("IntegrationTestConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:IntegrationTestConnection is required for integration tests.");
        var signingKey = values["Jwt:SigningKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SigningKey is required for integration tests.");
        var seedPassword = values["SeedUsers:DefaultPassword"]
            ?? throw new InvalidOperationException(
                "SeedUsers:DefaultPassword is required for integration tests.");
        EnsureSafeDatabase(connectionString);

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting(
            "Jwt:Issuer",
            values["Jwt:Issuer"] ?? "ConstructionInspection.IntegrationTests");
        builder.UseSetting(
            "Jwt:Audience",
            values["Jwt:Audience"] ?? "ConstructionInspection.IntegrationTests.Client");
        builder.UseSetting("Jwt:SigningKey", signingKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "5");
        builder.UseSetting("SeedUsers:DefaultPassword", seedPassword);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IObjectStorage>();
            services.RemoveAll<IPdfMetadataReader>();
            services.AddSingleton<FakeObjectStorage>();
            services.AddSingleton<IObjectStorage>(provider => provider.GetRequiredService<FakeObjectStorage>());
            services.AddSingleton<IPdfMetadataReader, FakePdfMetadataReader>();
        });
    }

    public HttpClient CreateApiClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });

    public async Task ResetDatabaseAsync()
    {
        await DatabaseResetLock.WaitAsync();
        try
        {
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            EnsureSafeDatabase(context.Database.GetConnectionString());
            await context.Database.EnsureDeletedAsync();
            await context.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<DevelopmentIdentitySeeder>().SeedAsync();
            scope.ServiceProvider.GetRequiredService<FakeObjectStorage>().Reset();
        }
        finally
        {
            DatabaseResetLock.Release();
        }
    }

    public sealed class FakeObjectStorage : IObjectStorage
    {
        private readonly Dictionary<string, byte[]> _objects = [];
        public async Task UploadAsync(string key, Stream content, long length, string contentType, CancellationToken ct)
        {
            using var output = new MemoryStream();
            await content.CopyToAsync(output, ct);
            _objects[key] = output.ToArray();
        }
        public async Task CopyToAsync(string key, Stream destination, CancellationToken ct) =>
            await destination.WriteAsync(_objects[key], ct);
        public Task DeleteAsync(string key, CancellationToken ct) { _objects.Remove(key); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(_objects.ContainsKey(key));
        public void Reset() => _objects.Clear();
    }

    private sealed class FakePdfMetadataReader : IPdfMetadataReader
    {
        public Task<PdfMetadata> ReadAsync(Stream content, CancellationToken ct) => Task.FromResult(new PdfMetadata(2));
    }

    public async Task<string> LoginAsync(HttpClient client, string email)
    {
        await using var scope = Services.CreateAsyncScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var password = configuration["SeedUsers:DefaultPassword"]
            ?? throw new InvalidOperationException("SeedUsers:DefaultPassword is required for integration tests.");

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("accessToken").GetString()
            ?? throw new InvalidOperationException("Login response did not contain an access token.");
    }

    public static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public async Task ExecuteDatabaseAsync(Func<ApplicationDbContext, Task> operation)
    {
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        EnsureSafeDatabase(context.Database.GetConnectionString());
        await operation(context);
    }

    private static void EnsureSafeDatabase(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The integration-test connection string is missing.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(
                builder.InitialCatalog,
                ExpectedDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing integration-test database mutation: Initial Catalog must be '{ExpectedDatabaseName}'.");
        }
    }
}
