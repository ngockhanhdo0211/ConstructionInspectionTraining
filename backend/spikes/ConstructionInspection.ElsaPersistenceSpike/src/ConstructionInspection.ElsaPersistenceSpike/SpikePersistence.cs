using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.ElsaPersistenceSpike;

public sealed class SpikeInboxMessage
{
    public Guid Id { get; set; }
    public Guid DomainEventId { get; set; }
    public Guid InspectionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string Outcome { get; set; } = "Pending";
    public string? ErrorCode { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SpikeWorkflowMapping
{
    public Guid InspectionId { get; set; }
    public string WorkflowInstanceId { get; set; } = string.Empty;
    public Guid? LastDomainEventId { get; set; }
    public string CurrentShadowState { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SpikeDbContext(DbContextOptions<SpikeDbContext> options) : DbContext(options)
{
    public DbSet<SpikeInboxMessage> InboxMessages => Set<SpikeInboxMessage>();
    public DbSet<SpikeWorkflowMapping> WorkflowMappings => Set<SpikeWorkflowMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpikeInboxMessage>(entity =>
        {
            entity.ToTable("SpikeInboxMessages");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.DomainEventId).IsUnique().HasDatabaseName("UX_SpikeInboxMessages_DomainEventId");
            entity.Property(x => x.EventType).HasMaxLength(80).IsUnicode(false).IsRequired();
            entity.Property(x => x.PayloadJson).HasMaxLength(16384).IsRequired();
            entity.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(x => x.ErrorCode).HasMaxLength(80).IsUnicode(false);
            entity.Property(x => x.ReceivedAtUtc).HasPrecision(7).IsRequired();
            entity.Property(x => x.ProcessedAtUtc).HasPrecision(7);
            entity.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<SpikeWorkflowMapping>(entity =>
        {
            entity.ToTable("SpikeWorkflowMappings");
            entity.HasKey(x => x.InspectionId);
            entity.HasIndex(x => x.WorkflowInstanceId).IsUnique().HasDatabaseName("UX_SpikeWorkflowMappings_WorkflowInstanceId");
            entity.Property(x => x.WorkflowInstanceId).HasMaxLength(255).IsUnicode(false).IsRequired();
            entity.Property(x => x.CurrentShadowState).HasMaxLength(32).IsUnicode(false).IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasPrecision(7).IsRequired();
            entity.Property(x => x.RowVersion).IsRowVersion();
        });
    }
}

public static class SpikeDatabaseSafety
{
    public const string ExpectedDatabaseName = "ConstructionInspectionElsaSpikeDb";

    public static string RequireSafeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings__ElsaSpikeConnection is required.");
        if (connectionString.Contains("$(", StringComparison.Ordinal)
            || connectionString.Contains("${", StringComparison.Ordinal)
            || connectionString.Contains('%'))
            throw new InvalidOperationException("The spike connection string contains an unresolved variable.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.InitialCatalog, ExpectedDatabaseName, StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing spike database access: Initial Catalog must be '{ExpectedDatabaseName}'.");
        return connectionString;
    }
}

public static class SpikeSchemaInitializer
{
    public static async Task InitializeAsync(string connectionString, CancellationToken cancellationToken)
    {
        SpikeDatabaseSafety.RequireSafeConnectionString(connectionString);
        const string sql = """
            IF OBJECT_ID(N'[dbo].[SpikeInboxMessages]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SpikeInboxMessages](
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_SpikeInboxMessages] PRIMARY KEY,
                    [DomainEventId] uniqueidentifier NOT NULL,
                    [InspectionId] uniqueidentifier NOT NULL,
                    [EventType] varchar(80) NOT NULL,
                    [PayloadJson] nvarchar(max) NOT NULL,
                    [PayloadHash] varchar(64) NOT NULL,
                    [ReceivedAtUtc] datetime2(7) NOT NULL,
                    [ProcessedAtUtc] datetime2(7) NULL,
                    [Outcome] varchar(32) NOT NULL,
                    [ErrorCode] varchar(80) NULL,
                    [RowVersion] rowversion NOT NULL,
                    CONSTRAINT [CK_SpikeInboxMessages_PayloadJson] CHECK (ISJSON([PayloadJson]) = 1)
                );
                CREATE UNIQUE INDEX [UX_SpikeInboxMessages_DomainEventId]
                    ON [dbo].[SpikeInboxMessages]([DomainEventId]);
            END;

            IF OBJECT_ID(N'[dbo].[SpikeWorkflowMappings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[SpikeWorkflowMappings](
                    [InspectionId] uniqueidentifier NOT NULL CONSTRAINT [PK_SpikeWorkflowMappings] PRIMARY KEY,
                    [WorkflowInstanceId] varchar(255) NOT NULL,
                    [LastDomainEventId] uniqueidentifier NULL,
                    [CurrentShadowState] varchar(32) NOT NULL,
                    [UpdatedAtUtc] datetime2(7) NOT NULL,
                    [RowVersion] rowversion NOT NULL
                );
                CREATE UNIQUE INDEX [UX_SpikeWorkflowMappings_WorkflowInstanceId]
                    ON [dbo].[SpikeWorkflowMappings]([WorkflowInstanceId]);
            END;
            """;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!string.Equals(connection.Database, SpikeDatabaseSafety.ExpectedDatabaseName, StringComparison.Ordinal))
            throw new InvalidOperationException("The connected database failed the spike safety guard.");
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed record SpikeDatabaseInventory(
    IReadOnlyList<string> Tables,
    long DatabaseSizeBytes,
    long UsedTableBytes,
    long InboxMessageCount,
    long WorkflowMappingCount);

public static class SpikePersistenceDiagnostics
{
    public static async Task<SpikeDatabaseInventory> ReadInventoryAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        SpikeDatabaseSafety.RequireSafeConnectionString(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!string.Equals(connection.Database, SpikeDatabaseSafety.ExpectedDatabaseName, StringComparison.Ordinal))
            throw new InvalidOperationException("The connected database failed the spike safety guard.");

        var tables = new List<string>();
        await using (var command = new SqlCommand(
                         "SELECT [name] FROM sys.tables ORDER BY [name];",
                         connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                tables.Add(reader.GetString(0));
        }

        const string metricsSql = """
            SELECT
                (SELECT SUM(CAST([size] AS bigint)) * 8192 FROM sys.database_files),
                (SELECT SUM(CAST([used_page_count] AS bigint)) * 8192 FROM sys.dm_db_partition_stats),
                (SELECT COUNT_BIG(*) FROM [dbo].[SpikeInboxMessages]),
                (SELECT COUNT_BIG(*) FROM [dbo].[SpikeWorkflowMappings]);
            """;
        await using var metrics = new SqlCommand(metricsSql, connection);
        await using var metricsReader = await metrics.ExecuteReaderAsync(cancellationToken);
        await metricsReader.ReadAsync(cancellationToken);
        return new SpikeDatabaseInventory(
            tables,
            metricsReader.GetInt64(0),
            metricsReader.GetInt64(1),
            metricsReader.GetInt64(2),
            metricsReader.GetInt64(3));
    }
}
