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
using Microsoft.AspNetCore.Identity;

namespace ConstructionInspection.IntegrationTests.ConstructionObjects;

public sealed class ConstructionObjectApiFactory : WebApplicationFactory<Program>
{
    private const string ExpectedDatabaseName = "ConstructionInspectionIntegrationTestDb";
    private static readonly SemaphoreSlim DatabaseResetLock = new(1, 1);
    private static bool databaseInitialized;

    private const string ClearDatabaseSql = """
        DELETE FROM [ProcessHistories];
        DELETE FROM [BusinessProcesses];
        DELETE FROM [RfiEntries];
        DELETE FROM [Rfis];
        UPDATE [Checklists] SET [CurrentSubmissionId] = NULL;
        DELETE FROM [ChecklistSubmissionInspections];
        DELETE FROM [ChecklistSubmissionItems];
        DELETE FROM [ChecklistInspections];
        DELETE FROM [ChecklistSubmissions];
        DELETE FROM [ChecklistItems];
        DELETE FROM [Checklists];
        UPDATE [Inspections] SET [CurrentSubmissionId] = NULL;
        DELETE FROM [InspectionSubmissions];
        DELETE FROM [Inspections];
        DELETE FROM [DrawingObjectMappings];
        DELETE FROM [DrawingDocuments];
        DELETE FROM [FormTemplateVersions];
        DELETE FROM [FormTemplates];
        UPDATE [ConstructionObjects] SET [ParentId] = NULL;
        DELETE FROM [ConstructionObjects];
        DELETE FROM [AspNetUserTokens];
        DELETE FROM [AspNetUserLogins];
        DELETE FROM [AspNetUserClaims];
        DELETE FROM [AspNetRoleClaims];
        DELETE FROM [AspNetUserRoles];
        DELETE FROM [AspNetUsers]
        WHERE [NormalizedEmail] NOT IN (
            N'ADMINISTRATOR@LOCAL.TEST', N'MANAGER@LOCAL.TEST',
            N'INSPECTOR@LOCAL.TEST', N'REVIEWER@LOCAL.TEST');
        UPDATE [AspNetUsers]
        SET [IsActive] = 1, [AccessFailedCount] = 0, [LockoutEnd] = NULL
        WHERE [NormalizedEmail] IN (
            N'ADMINISTRATOR@LOCAL.TEST', N'MANAGER@LOCAL.TEST',
            N'INSPECTOR@LOCAL.TEST', N'REVIEWER@LOCAL.TEST');
        INSERT INTO [AspNetUserRoles] ([UserId], [RoleId])
        SELECT u.[Id], r.[Id]
        FROM (VALUES
            (N'ADMINISTRATOR@LOCAL.TEST', N'ADMINISTRATOR'),
            (N'MANAGER@LOCAL.TEST', N'MANAGER'),
            (N'INSPECTOR@LOCAL.TEST', N'INSPECTOR'),
            (N'REVIEWER@LOCAL.TEST', N'REVIEWER')
        ) expected([NormalizedEmail], [NormalizedRole])
        JOIN [AspNetUsers] u ON u.[NormalizedEmail] = expected.[NormalizedEmail]
        JOIN [AspNetRoles] r ON r.[NormalizedName] = expected.[NormalizedRole];
        ALTER SEQUENCE [InspectionNumberSequence] RESTART WITH 1;
        ALTER SEQUENCE [ChecklistNumberSequence] RESTART WITH 1;
        ALTER SEQUENCE [RfiNumberSequence] RESTART WITH 1;
        """;

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
            if (!databaseInitialized)
            {
                await InitializeDatabaseAsync();
                await using var seedScope = Services.CreateAsyncScope();
                await seedScope.ServiceProvider.GetRequiredService<DevelopmentIdentitySeeder>().SeedAsync();
                databaseInitialized = true;
            }
            else
            {
                await using var clearScope = Services.CreateAsyncScope();
                var clearContext = clearScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                EnsureSafeDatabase(clearContext.Database.GetConnectionString());
                await clearContext.Database.ExecuteSqlRawAsync(ClearDatabaseSql);
                clearContext.ChangeTracker.Clear();
            }

            Services.GetRequiredService<FakeObjectStorage>().Reset();
        }
        finally
        {
            DatabaseResetLock.Release();
        }
    }

    private async Task InitializeDatabaseAsync()
    {
        SqlConnection.ClearAllPools();
        await using var migrationScope = Services.CreateAsyncScope();
        var migrationContext = migrationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        EnsureSafeDatabase(migrationContext.Database.GetConnectionString());
        await migrationContext.Database.MigrateAsync();
        await migrationContext.Database.ExecuteSqlRawAsync(ClearDatabaseSql);
        migrationContext.ChangeTracker.Clear();
    }

    public sealed class FakeObjectStorage : IObjectStorage
    {
        private readonly Dictionary<string, byte[]> _objects = [];
        private readonly List<(long Offset, long Length)> _ranges = [];
        public IReadOnlyList<(long Offset, long Length)> Ranges => _ranges;
        public int CallCount { get; private set; }
        public bool BlockCopies { get; set; }
        public bool SawCancellation { get; private set; }
        public async Task UploadAsync(string key, Stream content, long length, string contentType, CancellationToken ct)
        {
            CallCount++;
            using var output = new MemoryStream();
            await content.CopyToAsync(output, ct);
            _objects[key] = output.ToArray();
        }
        public async Task CopyToAsync(string key, Stream destination, CancellationToken ct)
        { CallCount++; await destination.WriteAsync(_objects[key], ct); }
        public Task DeleteAsync(string key, CancellationToken ct) { CallCount++; _objects.Remove(key); return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string key, CancellationToken ct) { CallCount++; return Task.FromResult(_objects.ContainsKey(key)); }
        public Task<ObjectStorageMetadata?> GetMetadataAsync(string key, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_objects.TryGetValue(key, out var value)
                ? new ObjectStorageMetadata(value.LongLength, new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero))
                : null);
        }
        public async Task CopyRangeToAsync(string key, Stream destination, long offset, long length, CancellationToken ct)
        {
            CallCount++;
            _ranges.Add((offset, length));
            if (BlockCopies)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { SawCancellation = true; throw; }
            }
            if (!_objects.TryGetValue(key, out var value))
                throw new ObjectStorageException("drawing.storage_object_missing", "The Drawing object is missing from storage.");
            await destination.WriteAsync(value.AsMemory(checked((int)offset), checked((int)length)), ct);
        }
        public void RemoveAll() => _objects.Clear();
        public void Reset()
        {
            _objects.Clear();
            _ranges.Clear();
            BlockCopies = false;
            SawCancellation = false;
            CallCount = 0;
        }
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

    public async Task<Guid> CreateUserAsync(string email, string fullName, string role)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var password = configuration["SeedUsers:DefaultPassword"]
            ?? throw new InvalidOperationException("SeedUsers:DefaultPassword is required for integration tests.");
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            IsActive = true,
            LockoutEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        EnsureSucceeded(await userManager.CreateAsync(user, password));
        EnsureSucceeded(await userManager.AddToRoleAsync(user, role));
        return user.Id;
    }

    public async Task SetUserActiveAsync(string email, bool isActive)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("Integration-test user was not found.");
        user.IsActive = isActive;
        EnsureSucceeded(await userManager.UpdateAsync(user));
    }

    public async Task RemoveUserRoleAsync(string email, string role)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("Integration-test user was not found.");
        EnsureSucceeded(await userManager.RemoveFromRoleAsync(user, role));
    }

    public async Task AddUserRoleAsync(string email, string role)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("Integration-test user was not found.");
        EnsureSucceeded(await userManager.AddToRoleAsync(user, role));
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Integration-test Identity operation failed: {string.Join(",", result.Errors.Select(x => x.Code))}");
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
