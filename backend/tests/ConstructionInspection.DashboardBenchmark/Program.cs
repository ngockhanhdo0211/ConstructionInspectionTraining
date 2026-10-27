using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.IntegrationTests.ConstructionObjects;
using ConstructionInspection.IntegrationTests.Support;
using ConstructionInspection.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

const string BenchmarkConnectionName = "DashboardBenchmarkConnection";
const string BenchmarkDatabaseName = "ConstructionInspectionDashboardBenchmarkDb";
const int RecordCount = 10_000;
const int Concurrency = 30;
const int SamplesPerRole = 120;
const string ExperimentalCreatorIndexName = "IX_BusinessProcesses_Dashboard_Creator_AB_EXPERIMENTAL";
const string EvidenceDirectory = @"D:\DashboardBenchmarkEvidence_20261027";
var nowUtc = new DateTime(2026, 10, 27, 6, 0, 0, DateTimeKind.Utc);

var benchmarkConnection = Environment.GetEnvironmentVariable(
    "ConnectionStrings__DashboardBenchmarkConnection");
EnsureBenchmarkDatabase(benchmarkConnection);
Console.WriteLine($"database_guard=PASS database={BenchmarkDatabaseName}");
await using var factory = ConstructionObjectApiFactory.CreateForDatabase(
    BenchmarkConnectionName,
    BenchmarkDatabaseName,
    benchmarkConnection!);

if (args.Contains("--creator-ab", StringComparer.Ordinal))
{
    await RunCreatorIndexAbAsync(factory, benchmarkConnection!, nowUtc);
    return;
}

if (args.Contains("--analyze-existing", StringComparer.Ordinal))
{
    factory.Services.GetRequiredService<TestClock>().Set(nowUtc);
    var existingEvidence = await CollectPlanEvidenceAsync(factory);
    foreach (var evidence in existingEvidence)
    {
        Console.WriteLine(
            $"plan={evidence.Label} sql_sha256={evidence.SqlSha256} sql_chars={evidence.SqlChars} " +
            $"logical_reads={evidence.LogicalReads} cpu_ms={evidence.CpuMilliseconds} " +
            $"elapsed_ms={evidence.ElapsedMilliseconds} seeks={evidence.IndexSeeks} " +
            $"scans={evidence.IndexScans} table_scans={evidence.TableScans} " +
            $"key_lookups={evidence.KeyLookups} sorts={evidence.Sorts} spills={evidence.Spills} " +
            $"requested_memory_kb={evidence.RequestedMemoryKb} granted_memory_kb={evidence.GrantedMemoryKb} " +
            $"max_estimate_actual_ratio={evidence.MaxEstimateActualRatio:F2} " +
            $"reads_by_table={evidence.ReadsByTable} used_indexes={evidence.UsedIndexes} " +
            $"missing_indexes={evidence.MissingIndexes}");
    }
    return;
}

var runConcurrencyLadder = args.Contains("--ladder-existing", StringComparer.Ordinal);
var useExistingDataset = args.Contains("--benchmark-existing", StringComparer.Ordinal)
    || runConcurrencyLadder;
double? migrationMilliseconds = null;
double? seedMilliseconds = null;
if (!useExistingDataset)
{
    var migrateWatch = Stopwatch.StartNew();
    await factory.ResetDatabaseAsync();
    migrateWatch.Stop();
    migrationMilliseconds = migrateWatch.Elapsed.TotalMilliseconds;
    factory.Services.GetRequiredService<TestClock>().Set(nowUtc);

    var users = await GetSeedUsersAsync(factory);
    var extraInspector1 = await factory.CreateUserAsync(
        "dashboard-benchmark-inspector-1@local.test", "Benchmark Inspector 1", ApplicationRoles.Inspector);
    var extraInspector2 = await factory.CreateUserAsync(
        "dashboard-benchmark-inspector-2@local.test", "Benchmark Inspector 2", ApplicationRoles.Inspector);
    var extraReviewer1 = await factory.CreateUserAsync(
        "dashboard-benchmark-reviewer-1@local.test", "Benchmark Reviewer 1", ApplicationRoles.Reviewer);
    var extraReviewer2 = await factory.CreateUserAsync(
        "dashboard-benchmark-reviewer-2@local.test", "Benchmark Reviewer 2", ApplicationRoles.Reviewer);

    var seedWatch = Stopwatch.StartNew();
    await factory.ExecuteDatabaseAsync(context => SeedAsync(
        context,
        nowUtc,
        users.Administrator,
        [users.Inspector, extraInspector1, extraInspector2],
        [users.Reviewer, extraReviewer1, extraReviewer2]));
    seedWatch.Stop();
    seedMilliseconds = seedWatch.Elapsed.TotalMilliseconds;
}
else
{
    factory.Services.GetRequiredService<TestClock>().Set(nowUtc);
}

var seedEvidence = await ReadSeedEvidenceAsync(factory);
Require(seedEvidence.Inspections == RecordCount, "Inspection seed count is incorrect.");
Require(seedEvidence.Checklists == RecordCount, "Checklist seed count is incorrect.");
Require(seedEvidence.Rfis == RecordCount, "RFI seed count is incorrect.");
Require(seedEvidence.InvalidOwners == 0, "BusinessProcess XOR owner guard failed.");
Require(seedEvidence.InspectionProcesses == RecordCount, "Inspection process count is incorrect.");
Require(seedEvidence.ChecklistProcesses == RecordCount, "Checklist process count is incorrect.");
Require(seedEvidence.RfiProcesses == RecordCount, "RFI process count is incorrect.");

Console.WriteLine("DASHBOARD_BENCHMARK");
Console.WriteLine($"dataset_mode={(useExistingDataset ? "existing" : "reset-and-seed")}");
Console.WriteLine(migrationMilliseconds is null ? "migration=SKIPPED" : $"migration_ms={migrationMilliseconds:F1}");
Console.WriteLine(seedMilliseconds is null ? "seed=SKIPPED" : $"seed_ms={seedMilliseconds:F1}");
Console.WriteLine(
    $"seed_counts inspections={seedEvidence.Inspections} checklists={seedEvidence.Checklists} " +
    $"rfis={seedEvidence.Rfis} inspection_processes={seedEvidence.InspectionProcesses} " +
    $"checklist_processes={seedEvidence.ChecklistProcesses} rfi_processes={seedEvidence.RfiProcesses} " +
    $"invalid_owners={seedEvidence.InvalidOwners}");

var roles = new[]
{
    new RoleRun("Inspector", "inspector@local.test", 4),
    new RoleRun("Reviewer", "reviewer@local.test", 4),
    new RoleRun("Manager", "manager@local.test", 2),
    new RoleRun("Administrator", "administrator@local.test", 1)
};

if (runConcurrencyLadder)
{
    foreach (var concurrency in new[] { 1, 5, 10, 30 })
    {
        foreach (var role in roles)
        {
            using var client = factory.CreateApiClient();
            ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, role.Email));
            for (var warmup = 0; warmup < 5; warmup++)
                await RequireDashboardAsync(client, null);

            var sampleCount = concurrency == 30 ? 120 : 60;
            var samples = new List<RequestSample>(sampleCount);
            for (var offset = 0; offset < sampleCount; offset += concurrency)
            {
                var batchSize = Math.Min(concurrency, sampleCount - offset);
                var batch = Enumerable.Range(offset, batchSize)
                    .Select(index => MeasureAsync(factory, client, role.Name, index))
                    .ToArray();
                samples.AddRange(await Task.WhenAll(batch));
            }

            var result = Summarize(role, samples);
            WriteResult(result, $"ladder={concurrency}");
        }
    }
    return;
}

var results = new List<RoleResult>();
foreach (var role in roles)
{
    using var client = factory.CreateApiClient();
    ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, role.Email));
    for (var warmup = 0; warmup < 5; warmup++)
        await RequireDashboardAsync(client, null);

    var samples = new List<RequestSample>(SamplesPerRole);
    for (var offset = 0; offset < SamplesPerRole; offset += Concurrency)
    {
        var batch = Enumerable.Range(offset, Concurrency)
            .Select(index => MeasureAsync(factory, client, role.Name, index))
            .ToArray();
        samples.AddRange(await Task.WhenAll(batch));
    }

    var result = Summarize(role, samples);
    results.Add(result);
    WriteResult(result, "benchmark=final");
}

await VerifyCorrectnessAsync(factory, nowUtc);
Console.WriteLine("correctness=PASS");

var planEvidence = await CollectPlanEvidenceAsync(factory);
foreach (var evidence in planEvidence)
{
    Console.WriteLine(
        $"plan={evidence.Label} sql_sha256={evidence.SqlSha256} sql_chars={evidence.SqlChars} " +
        $"logical_reads={evidence.LogicalReads} cpu_ms={evidence.CpuMilliseconds} " +
        $"elapsed_ms={evidence.ElapsedMilliseconds} seeks={evidence.IndexSeeks} " +
        $"scans={evidence.IndexScans} table_scans={evidence.TableScans} " +
        $"key_lookups={evidence.KeyLookups} sorts={evidence.Sorts} spills={evidence.Spills} " +
        $"requested_memory_kb={evidence.RequestedMemoryKb} granted_memory_kb={evidence.GrantedMemoryKb} " +
        $"max_estimate_actual_ratio={evidence.MaxEstimateActualRatio:F2} " +
        $"reads_by_table={evidence.ReadsByTable} used_indexes={evidence.UsedIndexes} " +
        $"missing_indexes={evidence.MissingIndexes}");
}

var acceptanceFailures = new List<string>();
foreach (var result in results)
{
    if (result.Errors != 0) acceptanceFailures.Add($"{result.Role}:errors");
    if (result.P95 > 500) acceptanceFailures.Add($"{result.Role}:p95");
    if (result.MaxPayloadBytes >= 64 * 1024) acceptanceFailures.Add($"{result.Role}:payload");
    if (result.MaxCommands > result.CommandBudget) acceptanceFailures.Add($"{result.Role}:commands");
}

Console.WriteLine(acceptanceFailures.Count == 0
    ? "acceptance=PASS"
    : $"acceptance=FAIL reasons={string.Join(',', acceptanceFailures)}");

static async Task RunCreatorIndexAbAsync(
    ConstructionObjectApiFactory factory,
    string connectionString,
    DateTime nowUtc)
{
    const ulong minimumAvailableMemoryBytes = 2UL * 1024 * 1024 * 1024;
    var cleanupSql = $"DROP INDEX [{ExperimentalCreatorIndexName}] ON [dbo].[BusinessProcesses];";
    var runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
    Directory.CreateDirectory(EvidenceDirectory);
    var evidencePath = Path.Combine(EvidenceDirectory, $"creator-ab-{runId}.json");
    var document = new CreatorAbEvidenceDocument
    {
        RunId = runId,
        Database = BenchmarkDatabaseName,
        SamplesPerRole = SamplesPerRole,
        Concurrency = Concurrency,
        WarmupsPerRole = 5,
        ExperimentalIndex = ExperimentalCreatorIndexName,
        Status = "RUNNING"
    };
    var createdIndex = false;
    Exception? cleanupFailure = null;

    try
    {
        var availableMemoryBytes = GetAvailablePhysicalMemoryBytes();
        document.AvailableMemoryMiB = Math.Round(availableMemoryBytes / 1024d / 1024d, 1);
        Console.WriteLine(
            $"creator_ab_preflight available_memory_mib={document.AvailableMemoryMiB:F1} " +
            $"required_memory_mib={minimumAvailableMemoryBytes / 1024 / 1024}");
        Require(availableMemoryBytes >= minimumAvailableMemoryBytes,
            "Creator A/B requires at least 2 GiB of available physical memory before any mutation.");

        var preflight = await ReadCreatorAbPreflightAsync(connectionString);
        document.Preflight = preflight;
        Console.WriteLine(
            $"creator_ab_preflight database_guard=PASS encrypted_connection=PASS " +
            $"inspections={preflight.Inspections} checklists={preflight.Checklists} " +
            $"rfis={preflight.Rfis} invalid_owners={preflight.InvalidOwners} " +
            $"latest_migration={preflight.LatestMigration}");
        Require(preflight.Inspections == RecordCount, "Inspection count must be exactly 10,000.");
        Require(preflight.Checklists == RecordCount, "Checklist count must be exactly 10,000.");
        Require(preflight.Rfis == RecordCount, "RFI count must be exactly 10,000.");
        Require(preflight.InvalidOwners == 0, "BusinessProcess XOR owner guard failed.");
        if (preflight.ExperimentalIndexExists)
        {
            Console.WriteLine("creator_ab_preflight=FAIL reason=stale_experimental_index");
            Console.WriteLine($"cleanup_command={cleanupSql}");
            throw new InvalidOperationException(
                "The experimental creator index already exists. Run the printed cleanup command explicitly before retrying.");
        }

        factory.Services.GetRequiredService<TestClock>().Set(nowUtc);
        var baseline = await RunCreatorAbPhaseAsync(factory, "baseline", nowUtc);
        document.Phases.Add(baseline);
        await WriteCreatorAbEvidenceAsync(evidencePath, document);

        await ExecuteIndexCommandAsync(connectionString, $"""
            CREATE INDEX [{ExperimentalCreatorIndexName}]
            ON [dbo].[BusinessProcesses]
            (
                [CreatedByUserId],
                [EntityType],
                [Status],
                [UpdatedAt],
                [Id]
            )
            INCLUDE ([InspectionId], [ChecklistId], [RfiId], [DueAt], [CreatedAt])
            WHERE [CreatedByUserId] IS NOT NULL;
            """);
        createdIndex = true;
        Console.WriteLine($"experimental_index=CREATED name={ExperimentalCreatorIndexName}");

        var afterIndex1 = await RunCreatorAbPhaseAsync(factory, "after-index-1", nowUtc);
        document.Phases.Add(afterIndex1);
        await WriteCreatorAbEvidenceAsync(evidencePath, document);

        var afterIndex2 = await RunCreatorAbPhaseAsync(factory, "after-index-2", nowUtc);
        document.Phases.Add(afterIndex2);
        document.ExperimentalIndexUsed = afterIndex1.Plans.Concat(afterIndex2.Plans)
            .Any(plan => plan.UsedIndexes.Contains(ExperimentalCreatorIndexName, StringComparison.Ordinal));
        document.ProductionLikeGate = EvaluateCreatorAbGate([afterIndex1, afterIndex2], 500);
        document.LocalStressGate = EvaluateCreatorAbGate([afterIndex1, afterIndex2], 2_000);
        document.Status = "COMPLETE";
        Console.WriteLine($"experimental_index_used={document.ExperimentalIndexUsed}");
        Console.WriteLine($"production_like_p95_500_gate={document.ProductionLikeGate}");
        Console.WriteLine($"local_stress_p95_2000_gate={document.LocalStressGate}");
    }
    catch (Exception exception)
    {
        document.Status = "FAILED";
        document.FailureType = exception.GetType().Name;
        throw;
    }
    finally
    {
        if (createdIndex)
        {
            try
            {
                await ExecuteIndexCommandAsync(connectionString, cleanupSql);
                document.Cleanup = "PASS";
                Console.WriteLine($"experimental_index_cleanup=PASS name={ExperimentalCreatorIndexName}");
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
                document.Cleanup = "FAILED";
                document.Status = "FAILED";
                Console.WriteLine($"experimental_index_cleanup=FAIL name={ExperimentalCreatorIndexName}");
                Console.WriteLine($"cleanup_command={cleanupSql}");
            }
        }
        else
        {
            document.Cleanup = "NOT_CREATED";
        }

        await WriteCreatorAbEvidenceAsync(evidencePath, document);
        Console.WriteLine($"evidence={evidencePath}");
    }

    if (cleanupFailure is not null)
        throw new InvalidOperationException(
            "Experimental creator index cleanup failed. Run the printed cleanup command explicitly.",
            cleanupFailure);
}

static async Task<CreatorAbPhaseEvidence> RunCreatorAbPhaseAsync(
    ConstructionObjectApiFactory factory,
    string phase,
    DateTime nowUtc)
{
    Console.WriteLine($"creator_ab_phase={phase} status=STARTED");
    var roles = new[]
    {
        new RoleRun("Inspector", "inspector@local.test", 4),
        new RoleRun("Reviewer", "reviewer@local.test", 4),
        new RoleRun("Manager", "manager@local.test", 2),
        new RoleRun("Administrator", "administrator@local.test", 1)
    };
    var results = new List<RoleResult>(roles.Length);
    foreach (var role in roles)
    {
        using var client = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, role.Email));
        for (var warmup = 0; warmup < 5; warmup++)
            await RequireDashboardAsync(client, null);

        var samples = new List<RequestSample>(SamplesPerRole);
        for (var offset = 0; offset < SamplesPerRole; offset += Concurrency)
        {
            var batch = Enumerable.Range(offset, Concurrency)
                .Select(index => MeasureAsync(factory, client, role.Name, index))
                .ToArray();
            samples.AddRange(await Task.WhenAll(batch));
        }

        var result = Summarize(role, samples);
        results.Add(result);
        WriteResult(result, $"creator_ab_phase={phase}");
    }

    await VerifyCorrectnessAsync(factory, nowUtc);
    Console.WriteLine($"creator_ab_phase={phase} correctness=PASS");
    var plans = await CollectPlanEvidenceAsync(factory);
    foreach (var plan in plans)
    {
        Console.WriteLine(
            $"creator_ab_phase={phase} plan={plan.Label} logical_reads={plan.LogicalReads} " +
            $"cpu_ms={plan.CpuMilliseconds} elapsed_ms={plan.ElapsedMilliseconds} " +
            $"seeks={plan.IndexSeeks} scans={plan.IndexScans} table_scans={plan.TableScans} " +
            $"key_lookups={plan.KeyLookups} sorts={plan.Sorts} spills={plan.Spills} " +
            $"reads_by_table={plan.ReadsByTable} used_indexes={plan.UsedIndexes} " +
            $"missing_indexes={plan.MissingIndexes}");
    }

    Console.WriteLine($"creator_ab_phase={phase} status=COMPLETE");
    return new(phase, results, plans, true);
}

static string EvaluateCreatorAbGate(
    IEnumerable<CreatorAbPhaseEvidence> phases,
    double p95LimitMilliseconds)
{
    var failures = phases
        .SelectMany(phase => phase.Results.Select(result => (phase.Name, Result: result)))
        .Where(item => item.Result.Errors != 0
            || item.Result.P95 > p95LimitMilliseconds
            || item.Result.MaxPayloadBytes >= 64 * 1024
            || item.Result.MaxCommands > item.Result.CommandBudget)
        .Select(item => $"{item.Name}:{item.Result.Role}")
        .ToArray();
    return failures.Length == 0 ? "PASS" : $"FAIL:{string.Join(',', failures)}";
}

static async Task<CreatorAbPreflightEvidence> ReadCreatorAbPreflightAsync(string connectionString)
{
    EnsureBenchmarkDatabase(connectionString);
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        SELECT DB_NAME();
        SELECT [encrypt_option]
        FROM [sys].[dm_exec_connections]
        WHERE [session_id] = @@SPID;
        SELECT
            (SELECT COUNT_BIG(*) FROM [dbo].[Inspections]),
            (SELECT COUNT_BIG(*) FROM [dbo].[Checklists]),
            (SELECT COUNT_BIG(*) FROM [dbo].[Rfis]),
            (SELECT COUNT_BIG(*) FROM [dbo].[BusinessProcesses]
             WHERE (CASE WHEN [InspectionId] IS NULL THEN 0 ELSE 1 END
                  + CASE WHEN [ChecklistId] IS NULL THEN 0 ELSE 1 END
                  + CASE WHEN [RfiId] IS NULL THEN 0 ELSE 1 END) <> 1);
        SELECT TOP (1) [MigrationId]
        FROM [dbo].[__EFMigrationsHistory]
        ORDER BY [MigrationId] DESC;
        SELECT COUNT(*)
        FROM [sys].[indexes]
        WHERE [object_id] = OBJECT_ID(N'[dbo].[BusinessProcesses]')
          AND [name] = N'{ExperimentalCreatorIndexName}';
        """;
    await using var reader = await command.ExecuteReaderAsync();
    Require(await reader.ReadAsync(), "Database identity result is missing.");
    var database = reader.GetString(0);
    Require(string.Equals(database, BenchmarkDatabaseName, StringComparison.Ordinal),
        "Connected database identity does not exactly match the benchmark database.");

    Require(await reader.NextResultAsync() && await reader.ReadAsync(),
        "Encrypted connection result is missing.");
    Require(string.Equals(reader.GetString(0), "TRUE", StringComparison.OrdinalIgnoreCase),
        "Benchmark SQL connection is not encrypted.");

    Require(await reader.NextResultAsync() && await reader.ReadAsync(),
        "Dataset count result is missing.");
    var inspections = checked((int)reader.GetInt64(0));
    var checklists = checked((int)reader.GetInt64(1));
    var rfis = checked((int)reader.GetInt64(2));
    var invalidOwners = checked((int)reader.GetInt64(3));

    Require(await reader.NextResultAsync() && await reader.ReadAsync(),
        "Migration result is missing.");
    var latestMigration = reader.GetString(0);
    Require(string.Equals(latestMigration, "20261027170418_AddDashboardIndexes", StringComparison.Ordinal),
        "Benchmark database is not at AddDashboardIndexes.");

    Require(await reader.NextResultAsync() && await reader.ReadAsync(),
        "Experimental index result is missing.");
    var experimentalIndexExists = reader.GetInt32(0) != 0;
    return new(
        database,
        true,
        inspections,
        checklists,
        rfis,
        invalidOwners,
        latestMigration,
        experimentalIndexExists);
}

static async Task ExecuteIndexCommandAsync(string connectionString, string sql)
{
    EnsureBenchmarkDatabase(connectionString);
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

static ulong GetAvailablePhysicalMemoryBytes()
{
    var status = new MemoryStatusEx();
    if (!GlobalMemoryStatusEx(ref status))
        throw new InvalidOperationException(
            $"Could not read available physical memory. Win32 error: {Marshal.GetLastWin32Error()}.");
    return status.AvailablePhysicalMemory;
}

static Task WriteCreatorAbEvidenceAsync(string path, CreatorAbEvidenceDocument document) =>
    File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, new JsonSerializerOptions
    {
        WriteIndented = true
    }));

[DllImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

static async Task<SeedUsers> GetSeedUsersAsync(ConstructionObjectApiFactory factory)
{
    SeedUsers? result = null;
    await factory.ExecuteDatabaseAsync(async context =>
    {
        var rows = await context.Users
            .Where(user => user.NormalizedEmail == "ADMINISTRATOR@LOCAL.TEST"
                || user.NormalizedEmail == "MANAGER@LOCAL.TEST"
                || user.NormalizedEmail == "INSPECTOR@LOCAL.TEST"
                || user.NormalizedEmail == "REVIEWER@LOCAL.TEST")
            .ToDictionaryAsync(user => user.NormalizedEmail!, user => user.Id);
        result = new(
            rows["ADMINISTRATOR@LOCAL.TEST"],
            rows["MANAGER@LOCAL.TEST"],
            rows["INSPECTOR@LOCAL.TEST"],
            rows["REVIEWER@LOCAL.TEST"]);
    });
    return result ?? throw new InvalidOperationException("Seed users were not found.");
}

static async Task SeedAsync(
    ApplicationDbContext context,
    DateTime nowUtc,
    Guid administratorId,
    IReadOnlyList<Guid> inspectors,
    IReadOnlyList<Guid> reviewers)
{
    var connectionString = context.Database.GetConnectionString();
    EnsureBenchmarkDatabase(connectionString);
    var parameters = new[]
    {
        new SqlParameter("@now", nowUtc),
        new SqlParameter("@admin", administratorId),
        new SqlParameter("@i0", inspectors[0]),
        new SqlParameter("@i1", inspectors[1]),
        new SqlParameter("@i2", inspectors[2]),
        new SqlParameter("@r0", reviewers[0]),
        new SqlParameter("@r1", reviewers[1]),
        new SqlParameter("@r2", reviewers[2]),
        new SqlParameter("@seedOffset", 0),
        new SqlParameter("@project", Guid.Parse("A0000000-0000-0000-0000-000000000001")),
        new SqlParameter("@building", Guid.Parse("A0000000-0000-0000-0000-000000000002")),
        new SqlParameter("@floor", Guid.Parse("A0000000-0000-0000-0000-000000000003")),
        new SqlParameter("@room", Guid.Parse("A0000000-0000-0000-0000-000000000004")),
        new SqlParameter("@workItem", Guid.Parse("A0000000-0000-0000-0000-000000000005")),
        new SqlParameter("@template", Guid.Parse("B0000000-0000-0000-0000-000000000001")),
        new SqlParameter("@version", Guid.Parse("B0000000-0000-0000-0000-000000000002"))
    };

    const string sql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;

        INSERT INTO [ConstructionObjects]
            ([Id],[ParentId],[Code],[Name],[ObjectType],[Location],[Description],[IsActive],[CreatedByUserId],[CreatedAt])
        VALUES
            (@project,NULL,N'BENCH-PROJECT',N'Benchmark Project','Project',NULL,NULL,1,@admin,@now),
            (@building,@project,N'BENCH-BUILDING',N'Benchmark Building','Building',NULL,NULL,1,@admin,@now),
            (@floor,@building,N'BENCH-FLOOR',N'Benchmark Floor','Floor',NULL,NULL,1,@admin,@now),
            (@room,@floor,N'BENCH-ROOM',N'Benchmark Room','Room',NULL,NULL,1,@admin,@now),
            (@workItem,@room,N'BENCH-WORK',N'Benchmark Work Item','WorkItem',NULL,NULL,1,@admin,@now);

        INSERT INTO [FormTemplates]
            ([Id],[Code],[Name],[Description],[Status],[NextVersionNumber],[CreatedByUserId],[CreatedAt])
        VALUES (@template,N'BENCH-TEMPLATE',N'Benchmark Template',NULL,'Active',2,@admin,@now);
        INSERT INTO [FormTemplateVersions]
            ([Id],[FormTemplateId],[VersionNumber],[Status],[SchemaJson],[CreatedByUserId],[CreatedAt],[PublishedByUserId],[PublishedAt])
        VALUES (@version,@template,1,'Published',N'{{"components":[]}}',@admin,@now,@admin,@now);

        -- BENCHMARK_BATCH

        INSERT INTO [Inspections]
            ([Id],[Number],[ConstructionObjectId],[FormTemplateVersionId],[Title],[Description],
             [CreatedByUserId],[ReviewerUserId],[CurrentSubmissionId],[NextRevisionNumber],[CreatedAt],[UpdatedAt])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('inspection:',n))),
            CONCAT('INS-2026-',RIGHT(CONCAT('000000',n),6)), @workItem, @version,
            CONCAT(N'Benchmark Inspection ',n), NULL,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,
            CASE WHEN n%6=0 THEN NULL WHEN n%3=0 THEN @r0 WHEN n%3=1 THEN @r1 ELSE @r2 END,
            NULL,2,DATEADD(minute,-n,@now),DATEADD(second,-n%3600,@now)
        FROM #N;

        INSERT INTO [InspectionSubmissions]
            ([Id],[InspectionId],[RevisionNumber],[DataJson],[CreatedByUserId],[CreatedAt],[UpdatedAt],[SubmittedAt])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('inspection-submission:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('inspection:',n))),1,N'{{}}',
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,
            DATEADD(minute,-n,@now),NULL,CASE WHEN n%6=0 THEN NULL ELSE DATEADD(second,-n%3600,@now) END
        FROM #N;

        -- BENCHMARK_BATCH

        UPDATE i SET [CurrentSubmissionId]=s.[Id]
        FROM [Inspections] i
        JOIN [InspectionSubmissions] s ON s.[InspectionId]=i.[Id]
        JOIN #N n ON i.[Id]=CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('inspection:',n.[n])));

        -- BENCHMARK_BATCH

        INSERT INTO [Checklists]
            ([Id],[Number],[ConstructionObjectId],[Title],[Description],[CreatedByUserId],[ReviewerUserId],
             [CurrentSubmissionId],[NextSubmissionNumber],[CreatedAt],[UpdatedAt])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist:',n))),
            CONCAT('CHK-2026-',RIGHT(CONCAT('000000',n),6)),@workItem,CONCAT(N'Benchmark Checklist ',n),NULL,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,
            CASE WHEN n%7 IN (0,1) THEN NULL WHEN n%3=0 THEN @r0 WHEN n%3=1 THEN @r1 ELSE @r2 END,
            NULL,CASE WHEN n%7 IN (0,1) THEN 1 ELSE 2 END,
            DATEADD(minute,-n,@now),DATEADD(second,-n%3600,@now)
        FROM #N;
        INSERT INTO [ChecklistItems]
            ([Id],[ChecklistId],[Sequence],[Criterion],[IsRequired],[Result],[Note],[CreatedByUserId],[CreatedAt])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist-item:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist:',n))),1,N'Benchmark criterion',1,
            CASE WHEN n%7 IN (0,1) THEN 'NotChecked' ELSE 'Pass' END,NULL,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,DATEADD(minute,-n,@now)
        FROM #N;
        INSERT INTO [ChecklistSubmissions] ([Id],[ChecklistId],[SubmissionNumber],[SubmittedByUserId],[SubmittedAt])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist-submission:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist:',n))),1,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,DATEADD(second,-n%3600,@now)
        FROM #N WHERE n%7 NOT IN (0,1);
        INSERT INTO [ChecklistSubmissionItems]
            ([Id],[ChecklistSubmissionId],[SourceChecklistItemId],[Sequence],[Criterion],[IsRequired],[Result],[Note])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist-snapshot:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist-submission:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist-item:',n))),1,N'Benchmark criterion',1,'Pass',NULL
        FROM #N WHERE n%7 NOT IN (0,1);

        -- BENCHMARK_BATCH

        UPDATE c SET [CurrentSubmissionId]=s.[Id]
        FROM [Checklists] c
        JOIN [ChecklistSubmissions] s ON s.[ChecklistId]=c.[Id]
        JOIN #N n ON c.[Id]=CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist:',n.[n])));

        -- BENCHMARK_BATCH

        INSERT INTO [Rfis]
            ([Id],[Number],[ConstructionObjectId],[Subject],[Question],[CreatedByUserId],[NextEntryNumber],
             [CreatedAt],[UpdatedAt],[UpdatedByUserId])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('rfi:',n))),
            CONCAT('RFI-2026-',RIGHT(CONCAT('000000',n),6)),@workItem,CONCAT(N'Benchmark RFI ',n),
            N'Benchmark content excluded from Dashboard',
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,
            CASE WHEN n%5 IN (2,3) THEN 2 ELSE 1 END,
            DATEADD(minute,-n,@now),DATEADD(second,-n%3600,@now),
            CASE WHEN n%5 IN (2,3) THEN CASE n%3 WHEN 0 THEN @r0 WHEN 1 THEN @r1 ELSE @r2 END ELSE NULL END
        FROM #N;
        INSERT INTO [RfiEntries] ([Id],[RfiId],[EntryNumber],[EntryType],[Content],[CreatedByUserId],[CreatedAtUtc])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('rfi-entry:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('rfi:',n))),1,'Response',N'Benchmark response',
            CASE n%3 WHEN 0 THEN @r0 WHEN 1 THEN @r1 ELSE @r2 END,DATEADD(second,-n%3600,@now)
        FROM #N WHERE n%5 IN (2,3);

        -- BENCHMARK_BATCH

        INSERT INTO [BusinessProcesses]
            ([Id],[InspectionId],[ChecklistId],[RfiId],[EntityType],[Status],[AssignedToUserId],
             [CreatedByUserId],[CreatedAt],[UpdatedAt],[DueAt],[CompletedAt])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('process-inspection:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('inspection:',n))),NULL,NULL,'Inspection',
            CASE n%6 WHEN 0 THEN 'Draft' WHEN 1 THEN 'Submitted' WHEN 2 THEN 'UnderReview'
                WHEN 3 THEN 'Approved' WHEN 4 THEN 'Recheck' ELSE 'Rejected' END,
            CASE WHEN n%6=0 THEN NULL WHEN n%3=0 THEN @r0 WHEN n%3=1 THEN @r1 ELSE @r2 END,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,
            DATEADD(minute,-n,@now),DATEADD(second,-n%3600,@now),NULL,
            CASE WHEN n%6 IN (3,5) THEN DATEADD(second,-n%3600,@now) ELSE NULL END
        FROM #N
        UNION ALL
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('process-checklist:',n))),
            NULL,CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('checklist:',n))),NULL,'Checklist',
            CASE n%7 WHEN 0 THEN 'Draft' WHEN 1 THEN 'InProgress' WHEN 2 THEN 'Submitted'
                WHEN 3 THEN 'UnderReview' WHEN 4 THEN 'Approved' WHEN 5 THEN 'Recheck' ELSE 'Rejected' END,
            CASE WHEN n%7 IN (0,1) THEN NULL WHEN n%3=0 THEN @r0 WHEN n%3=1 THEN @r1 ELSE @r2 END,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,
            DATEADD(minute,-n,@now),DATEADD(second,-n%3600,@now),NULL,
            CASE WHEN n%7 IN (4,6) THEN DATEADD(second,-n%3600,@now) ELSE NULL END
        FROM #N
        UNION ALL
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('process-rfi:',n))),
            NULL,NULL,CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('rfi:',n))),'Rfi',
            CASE n%5 WHEN 0 THEN 'Draft' WHEN 1 THEN 'Open' WHEN 2 THEN 'Responded'
                WHEN 3 THEN 'Closed' ELSE 'Cancelled' END,
            CASE WHEN n%5 IN (1,2,3) THEN
                CASE WHEN n%5=1 AND (n/5)%8=0 THEN @r2
                    WHEN n%3=0 THEN @r0 WHEN n%3=1 THEN @r1 ELSE @r2 END ELSE NULL END,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,
            DATEADD(minute,-n,@now),DATEADD(second,-n%3600,@now),
            CASE WHEN n%5=1 THEN CASE (n/5)%4 WHEN 0 THEN DATEADD(hour,-1,@now)
                WHEN 1 THEN @now WHEN 2 THEN DATEADD(hour,1,@now) ELSE NULL END ELSE NULL END,
            CASE WHEN n%5 IN (3,4) THEN DATEADD(second,-n%3600,@now) ELSE NULL END
        FROM #N;

        -- BENCHMARK_BATCH

        INSERT INTO [ProcessHistories]
            ([Id],[BusinessProcessId],[FromStatus],[ToStatus],[Action],[Note],[PerformedByUserId],
             [PerformedAt],[SubmissionId],[ChecklistSubmissionId],[RfiEntryId],[PreviousAssignedToUserId],[NewAssignedToUserId])
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('history-i-create:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('process-inspection:',n))),NULL,'Draft','Create',NULL,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,DATEADD(minute,-n,@now),NULL,NULL,NULL,NULL,NULL
        FROM #N
        UNION ALL
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('history-c-create:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('process-checklist:',n))),NULL,'Draft','Create',NULL,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,DATEADD(minute,-n,@now),NULL,NULL,NULL,NULL,NULL
        FROM #N
        UNION ALL
        SELECT CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('history-r-create:',n))),
            CONVERT(uniqueidentifier,HASHBYTES('MD5',CONCAT('process-rfi:',n))),NULL,'Draft','Create',NULL,
            CASE n%3 WHEN 0 THEN @i0 WHEN 1 THEN @i1 ELSE @i2 END,DATEADD(minute,-n,@now),NULL,NULL,NULL,NULL,NULL
        FROM #N;

        """;

    await context.Database.OpenConnectionAsync();
    await using var transaction = await context.Database.BeginTransactionAsync();
    try
    {
        var batches = sql.Split("-- BENCHMARK_BATCH", StringSplitOptions.RemoveEmptyEntries);
        const int chunkSize = 1_000;
        const string numbersSql = """
            CREATE TABLE #N ([n] int NOT NULL PRIMARY KEY);
            INSERT INTO #N ([n])
            SELECT TOP (1000) @seedOffset + ROW_NUMBER() OVER (ORDER BY a.[object_id], b.[object_id])
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            """;
        for (var index = 0; index < batches.Length; index++)
        {
            var usesNumbers = batches[index].Contains("#N", StringComparison.Ordinal);
            var chunks = usesNumbers ? RecordCount / chunkSize : 1;
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                var batchWatch = Stopwatch.StartNew();
                Console.WriteLine(
                    $"seed_batch={index + 1}/{batches.Length} chunk={chunk + 1}/{chunks} state=start");
                parameters.Single(parameter => parameter.ParameterName == "@seedOffset").Value = chunk * chunkSize;
                var batch = usesNumbers ? numbersSql + batches[index] : batches[index];
                await context.Database.ExecuteSqlRawAsync(batch, parameters);
                batchWatch.Stop();
                Console.WriteLine(
                    $"seed_batch={index + 1}/{batches.Length} chunk={chunk + 1}/{chunks} " +
                    $"state=complete elapsed_ms={batchWatch.Elapsed.TotalMilliseconds:F1}");
            }
        }
        await transaction.CommitAsync();
    }
    finally
    {
        await context.Database.CloseConnectionAsync();
    }
    context.ChangeTracker.Clear();
}

static async Task<SeedEvidence> ReadSeedEvidenceAsync(ConstructionObjectApiFactory factory)
{
    SeedEvidence? result = null;
    await factory.ExecuteDatabaseAsync(async context =>
    {
        var inspections = await context.Inspections.CountAsync();
        var checklists = await context.Checklists.CountAsync();
        var rfis = await context.Rfis.CountAsync();
        var inspectionProcesses = await context.BusinessProcesses.CountAsync(x => x.InspectionId != null);
        var checklistProcesses = await context.BusinessProcesses.CountAsync(x => x.ChecklistId != null);
        var rfiProcesses = await context.BusinessProcesses.CountAsync(x => x.RfiId != null);
        var invalidOwners = await context.BusinessProcesses.CountAsync(x =>
            (x.InspectionId != null ? 1 : 0) + (x.ChecklistId != null ? 1 : 0) + (x.RfiId != null ? 1 : 0) != 1);
        result = new(inspections, checklists, rfis, inspectionProcesses, checklistProcesses, rfiProcesses, invalidOwners);
    });
    return result ?? throw new InvalidOperationException("Seed evidence was unavailable.");
}

static async Task<RequestSample> MeasureAsync(
    ConstructionObjectApiFactory factory,
    HttpClient client,
    string role,
    int index)
{
    var correlation = $"{role}-{index}-{Guid.NewGuid():N}";
    var counter = factory.Services.GetRequiredService<SqlCommandCounter>();
    var timing = factory.Services.GetRequiredService<DashboardTimingCollector>();
    counter.Reset(correlation);
    timing.Reset(correlation);
    ThreadPool.GetAvailableThreads(out var workerBefore, out var ioBefore);
    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
    request.Headers.Add(SqlCommandCounter.CorrelationHeader, correlation);
    var watch = Stopwatch.StartNew();
    using var response = await client.SendAsync(request);
    var payload = await response.Content.ReadAsByteArrayAsync();
    watch.Stop();
    ThreadPool.GetAvailableThreads(out var workerAfter, out var ioAfter);
    var valid = response.StatusCode == HttpStatusCode.OK
        && response.Headers.CacheControl?.Private == true
        && response.Headers.CacheControl?.NoStore == true;
    if (valid)
        JsonDocument.Parse(payload).Dispose();
    var sqlMilliseconds = counter.GetDurations(correlation).Sum();
    var readServiceMilliseconds = timing.GetReadServiceMilliseconds(correlation);
    return new(
        watch.Elapsed.TotalMilliseconds,
        sqlMilliseconds,
        readServiceMilliseconds,
        Math.Max(0, watch.Elapsed.TotalMilliseconds - readServiceMilliseconds),
        payload.Length,
        counter.GetCount(correlation),
        Math.Min(workerBefore, workerAfter),
        Math.Min(ioBefore, ioAfter),
        valid);
}

static async Task RequireDashboardAsync(HttpClient client, string? correlation)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard");
    if (correlation is not null)
        request.Headers.Add(SqlCommandCounter.CorrelationHeader, correlation);
    using var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
    _ = await response.Content.ReadFromJsonAsync<JsonElement>();
}

static RoleResult Summarize(RoleRun role, IReadOnlyList<RequestSample> samples)
{
    var elapsed = samples.Select(sample => sample.ElapsedMilliseconds).Order().ToArray();
    var sql = samples.Select(sample => sample.SqlMilliseconds).Order().ToArray();
    var readService = samples.Select(sample => sample.ReadServiceMilliseconds).Order().ToArray();
    var overhead = samples.Select(sample => sample.HttpOverheadMilliseconds).Order().ToArray();
    var success = samples.Count(sample => sample.Valid);
    return new(
        role.Name,
        samples.Count,
        success,
        samples.Count - success,
        elapsed[0],
        Percentile(elapsed, 0.5),
        Percentile(elapsed, 0.95),
        elapsed[^1],
        elapsed.Average(),
        Percentile(sql, 0.95),
        Percentile(readService, 0.95),
        Percentile(overhead, 0.95),
        samples.Max(sample => sample.PayloadBytes),
        samples.Min(sample => sample.SqlCommands),
        samples.Max(sample => sample.SqlCommands),
        role.CommandBudget,
        samples.Min(sample => sample.AvailableWorkerThreads),
        samples.Min(sample => sample.AvailableIoThreads));
}

static void WriteResult(RoleResult result, string prefix) => Console.WriteLine(
    $"{prefix} role={result.Role} requests={result.Requests} success={result.Success} errors={result.Errors} " +
    $"min_ms={result.Min:F1} median_ms={result.Median:F1} p95_ms={result.P95:F1} " +
    $"max_ms={result.Max:F1} avg_ms={result.Average:F1} sql_p95_ms={result.SqlP95:F1} " +
    $"read_service_p95_ms={result.ReadServiceP95:F1} http_overhead_p95_ms={result.HttpOverheadP95:F1} " +
    $"payload_bytes={result.MaxPayloadBytes} sql_commands_min={result.MinCommands} " +
    $"sql_commands_max={result.MaxCommands} worker_available_min={result.AvailableWorkerThreadsMin} " +
    $"io_available_min={result.AvailableIoThreadsMin}");

static double Percentile(IReadOnlyList<double> sorted, double percentile) =>
    sorted[Math.Max(0, (int)Math.Ceiling(percentile * sorted.Count) - 1)];

static async Task VerifyCorrectnessAsync(ConstructionObjectApiFactory factory, DateTime nowUtc)
{
    foreach (var (email, expectedScope) in new[]
    {
        ("inspector@local.test", "Own"),
        ("reviewer@local.test", "Assigned"),
        ("manager@local.test", "Full"),
        ("administrator@local.test", "Full")
    })
    {
        using var client = factory.CreateApiClient();
        ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));
        using var response = await client.GetAsync("/api/dashboard");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<JsonElement>(body);
        Require(json.GetProperty("scopeMode").GetString() == expectedScope, "Scope mode mismatch.");
        Require(json.GetProperty("generatedAtUtc").GetDateTime() == nowUtc, "Clock snapshot mismatch.");
        Require(StatusSum(json, "inspectionSummary") == json.GetProperty("inspectionSummary").GetProperty("total").GetInt32(),
            "Inspection status sum mismatch.");
        Require(StatusSum(json, "checklistSummary") == json.GetProperty("checklistSummary").GetProperty("total").GetInt32(),
            "Checklist status sum mismatch.");
        Require(StatusSum(json, "rfiSummary") == json.GetProperty("rfiSummary").GetProperty("total").GetInt32(),
            "RFI status sum mismatch.");
        var queue = json.GetProperty("actionQueue").EnumerateArray().ToArray();
        Require(queue.Length <= 15, "Global queue limit exceeded.");
        Require(queue.GroupBy(item => item.GetProperty("module").GetString()).All(group => group.Count() <= 5),
            "Module queue limit exceeded.");
        Require(!body.Contains("dataJson", StringComparison.OrdinalIgnoreCase), "DataJson leaked.");
        Require(!body.Contains("schemaJson", StringComparison.OrdinalIgnoreCase), "SchemaJson leaked.");
        Require(!body.Contains("question", StringComparison.OrdinalIgnoreCase), "RFI question leaked.");
        Require(!body.Contains("storageKey", StringComparison.OrdinalIgnoreCase), "Storage key leaked.");
        Require(!body.Contains("url", StringComparison.OrdinalIgnoreCase), "Arbitrary URL leaked.");

        if (expectedScope == "Full")
        {
            Require(json.GetProperty("inspectionSummary").GetProperty("total").GetInt32() == RecordCount,
                "Full Inspection count mismatch.");
            Require(json.GetProperty("checklistSummary").GetProperty("total").GetInt32() == RecordCount,
                "Full Checklist count mismatch.");
            Require(json.GetProperty("rfiSummary").GetProperty("total").GetInt32() == RecordCount,
                "Full RFI count mismatch.");
        }
        if (email.StartsWith("administrator", StringComparison.Ordinal))
        {
            Require(queue.Length == 0, "Administrator queue must be empty.");
            Require(json.GetProperty("inspectionSummary").GetProperty("awaitingMyAction").GetInt32() == 0,
                "Administrator Inspection action count must be zero.");
            Require(json.GetProperty("checklistSummary").GetProperty("awaitingMyAction").GetInt32() == 0,
                "Administrator Checklist action count must be zero.");
            Require(json.GetProperty("rfiSummary").GetProperty("awaitingMyAction").GetInt32() == 0,
                "Administrator RFI action count must be zero.");
        }
    }

    await factory.ExecuteDatabaseAsync(async context =>
    {
        var strictOverdue = await context.BusinessProcesses.CountAsync(process =>
            process.RfiId != null && process.Status == ConstructionInspection.Domain.Inspections.ProcessStatus.Open
                && process.DueAt != null && process.DueAt < nowUtc);
        var boundary = await context.BusinessProcesses.CountAsync(process =>
            process.RfiId != null && process.Status == ConstructionInspection.Domain.Inspections.ProcessStatus.Open
                && process.DueAt == nowUtc);
        Require(strictOverdue > 0, "Overdue RFI seed is missing.");
        Require(boundary > 0, "DueAt boundary seed is missing.");
    });
}

static async Task<IReadOnlyList<PlanEvidence>> CollectPlanEvidenceAsync(
    ConstructionObjectApiFactory factory)
{
    var inspector = await CaptureCommandsAsync(factory, "inspector@local.test", "Inspector");
    var reviewer = await CaptureCommandsAsync(factory, "reviewer@local.test", "Reviewer");
    var manager = await CaptureCommandsAsync(factory, "manager@local.test", "Manager");

    var selected = new List<(string Label, SqlCommandSnapshot Snapshot)>
    {
        ("Combined summary full scope", FindSummary(manager)),
        ("Combined summary Inspector", FindSummary(inspector)),
        ("Combined summary Reviewer", FindSummary(reviewer)),
        ("RFI manager queue", FindQueue(manager, "[Rfis]")),
        ("Inspection inspector queue", FindQueue(inspector, "[Inspections]")),
        ("Checklist inspector queue", FindQueue(inspector, "[Checklists]")),
        ("RFI inspector queue", FindQueue(inspector, "[Rfis]")),
        ("Inspection reviewer queue", FindQueue(reviewer, "[Inspections]")),
        ("Checklist reviewer queue", FindQueue(reviewer, "[Checklists]")),
        ("RFI reviewer queue", FindQueue(reviewer, "[Rfis]"))
    };

    string? connectionString = null;
    await factory.ExecuteDatabaseAsync(context =>
    {
        connectionString = context.Database.GetConnectionString();
        return Task.CompletedTask;
    });
    EnsureBenchmarkDatabase(connectionString);

    var evidence = new List<PlanEvidence>(selected.Count);
    foreach (var item in selected)
        evidence.Add(await AnalyzeCommandAsync(connectionString!, item.Label, item.Snapshot));
    return evidence;

    static SqlCommandSnapshot FindSummary(IReadOnlyList<SqlCommandSnapshot> snapshots) =>
        snapshots.Single(snapshot =>
            snapshot.CommandText.Contains("[BusinessProcesses]", StringComparison.Ordinal)
            && !snapshot.CommandText.Contains("[ConstructionObjects]", StringComparison.Ordinal));

    static SqlCommandSnapshot FindQueue(
        IReadOnlyList<SqlCommandSnapshot> snapshots,
        string table) => snapshots.Single(snapshot =>
            snapshot.CommandText.Contains(table, StringComparison.Ordinal)
            && snapshot.CommandText.Contains("[ConstructionObjects]", StringComparison.Ordinal));
}

static async Task<IReadOnlyList<SqlCommandSnapshot>> CaptureCommandsAsync(
    ConstructionObjectApiFactory factory,
    string email,
    string role)
{
    using var client = factory.CreateApiClient();
    ConstructionObjectApiFactory.Authenticate(client, await factory.LoginAsync(client, email));
    var correlation = $"plan-{role}-{Guid.NewGuid():N}";
    var counter = factory.Services.GetRequiredService<SqlCommandCounter>();
    counter.Reset(correlation);
    await RequireDashboardAsync(client, correlation);
    return counter.GetSnapshots(correlation);
}

static async Task<PlanEvidence> AnalyzeCommandAsync(
    string connectionString,
    string label,
    SqlCommandSnapshot snapshot)
{
    var messages = new List<string>();
    string? planXml = null;
    await using var connection = new SqlConnection(connectionString);
    connection.InfoMessage += (_, args) => messages.Add(args.Message);
    await connection.OpenAsync();
    await using (var on = connection.CreateCommand())
    {
        on.CommandText = "SET STATISTICS IO ON; SET STATISTICS TIME ON; SET STATISTICS XML ON;";
        await on.ExecuteNonQueryAsync();
    }
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = snapshot.CommandText;
        foreach (var parameter in snapshot.Parameters)
        {
            var clone = parameter.SqlDbType is { } sqlDbType
                ? new SqlParameter(parameter.Name, sqlDbType)
                : new SqlParameter(parameter.Name, parameter.Value ?? DBNull.Value);
            clone.Value = parameter.Value ?? DBNull.Value;
            if (parameter.Size != 0) clone.Size = parameter.Size;
            command.Parameters.Add(clone);
        }
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync())
            {
                if (reader.FieldCount == 1 && reader.GetValue(0) is string value
                    && value.Contains("ShowPlanXML", StringComparison.Ordinal))
                    planXml = value;
            }
        } while (await reader.NextResultAsync());
    }
    await using (var off = connection.CreateCommand())
    {
        off.CommandText = "SET STATISTICS XML OFF; SET STATISTICS IO OFF; SET STATISTICS TIME OFF;";
        await off.ExecuteNonQueryAsync();
    }

    var message = string.Join(Environment.NewLine, messages);
    var logicalReads = Regex.Matches(message, @"logical reads (\d+)", RegexOptions.IgnoreCase)
        .Sum(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    var cpu = Regex.Matches(message, @"CPU time = (\d+) ms", RegexOptions.IgnoreCase)
        .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty().Max();
    var elapsed = Regex.Matches(message, @"elapsed time = (\d+) ms", RegexOptions.IgnoreCase)
        .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty().Max();

    var plan = string.IsNullOrWhiteSpace(planXml) ? null : XDocument.Parse(planXml);
    var relOps = plan?.Descendants().Where(element => element.Name.LocalName == "RelOp").ToArray() ?? [];
    var memory = plan?.Descendants().FirstOrDefault(element => element.Name.LocalName == "MemoryGrantInfo");
    var ratios = relOps.Select(relOp =>
    {
        var estimate = ParseDouble(relOp.Attribute("EstimateRows")?.Value);
        var actual = relOp.Descendants().Where(element => element.Name.LocalName == "RunTimeCountersPerThread")
            .Sum(element => ParseDouble(element.Attribute("ActualRows")?.Value));
        if (estimate <= 0 || actual <= 0) return 1d;
        return Math.Max(estimate / actual, actual / estimate);
    });
    var sqlHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.CommandText)));
    var readsByTable = Regex.Matches(
            message,
            @"Table '([^']+)'.*?logical reads (\d+)",
            RegexOptions.IgnoreCase)
        .Select(match => $"{match.Groups[1].Value}:{match.Groups[2].Value}")
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();
    var usedIndexes = plan?.Descendants()
        .Where(element => element.Name.LocalName == "Object")
        .Select(element => element.Attribute("Index")?.Value?.Trim('[', ']'))
        .Where(index => !string.IsNullOrWhiteSpace(index))
        .Cast<string>()
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray() ?? [];
    var missingIndexes = plan?.Descendants()
        .Where(element => element.Name.LocalName == "MissingIndex")
        .Select(FormatMissingIndex)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray() ?? [];
    return new(
        label,
        sqlHash,
        snapshot.CommandText.Length,
        logicalReads,
        cpu,
        elapsed,
        CountOperator(relOps, "Index Seek"),
        CountOperator(relOps, "Index Scan") + CountOperator(relOps, "Clustered Index Scan"),
        CountOperator(relOps, "Table Scan"),
        CountOperator(relOps, "Key Lookup"),
        CountOperator(relOps, "Sort"),
        plan?.Descendants().Count(element => element.Name.LocalName.Contains("Spill", StringComparison.OrdinalIgnoreCase)) ?? 0,
        ParseLong(memory?.Attribute("RequestedMemory")?.Value),
        ParseLong(memory?.Attribute("GrantedMemory")?.Value),
        ratios.DefaultIfEmpty(1d).Max(),
        string.Join(',', readsByTable),
        string.Join(',', usedIndexes),
        string.Join(';', missingIndexes));

    static int CountOperator(IEnumerable<XElement> relOps, string name) => relOps.Count(element =>
        string.Equals(element.Attribute("PhysicalOp")?.Value, name, StringComparison.Ordinal));
    static double ParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0;
    static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    static string FormatMissingIndex(XElement missingIndex)
    {
        var table = missingIndex.Attribute("Table")?.Value?.Trim('[', ']') ?? "unknown";
        var groups = missingIndex.Elements()
            .Where(element => element.Name.LocalName == "ColumnGroup")
            .Select(group =>
            {
                var usage = group.Attribute("Usage")?.Value ?? "unknown";
                var columns = group.Elements()
                    .Where(element => element.Name.LocalName == "Column")
                    .Select(element => element.Attribute("Name")?.Value?.Trim('[', ']'))
                    .Where(column => !string.IsNullOrWhiteSpace(column));
                return $"{usage}({string.Join(',', columns)})";
            });
        return $"{table}:{string.Join(',', groups)}";
    }
}

static int StatusSum(JsonElement root, string summaryName) =>
    root.GetProperty(summaryName).GetProperty("statusCounts").EnumerateObject()
        .Sum(property => property.Value.GetInt32());

static void EnsureBenchmarkDatabase(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Benchmark connection string is missing.");
    var builder = new SqlConnectionStringBuilder(connectionString);
    if (!string.Equals(builder.InitialCatalog, BenchmarkDatabaseName, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"Refusing mutation: parsed database name is '{builder.InitialCatalog}', expected exact benchmark database name.");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed record SeedUsers(Guid Administrator, Guid Manager, Guid Inspector, Guid Reviewer);
internal sealed record SeedEvidence(
    int Inspections,
    int Checklists,
    int Rfis,
    int InspectionProcesses,
    int ChecklistProcesses,
    int RfiProcesses,
    int InvalidOwners);
internal sealed record RoleRun(string Name, string Email, int CommandBudget);
internal sealed record RequestSample(
    double ElapsedMilliseconds,
    double SqlMilliseconds,
    double ReadServiceMilliseconds,
    double HttpOverheadMilliseconds,
    int PayloadBytes,
    int SqlCommands,
    int AvailableWorkerThreads,
    int AvailableIoThreads,
    bool Valid);
internal sealed record RoleResult(
    string Role,
    int Requests,
    int Success,
    int Errors,
    double Min,
    double Median,
    double P95,
    double Max,
    double Average,
    double SqlP95,
    double ReadServiceP95,
    double HttpOverheadP95,
    int MaxPayloadBytes,
    int MinCommands,
    int MaxCommands,
    int CommandBudget,
    int AvailableWorkerThreadsMin,
    int AvailableIoThreadsMin);
internal sealed record PlanEvidence(
    string Label,
    string SqlSha256,
    int SqlChars,
    int LogicalReads,
    int CpuMilliseconds,
    int ElapsedMilliseconds,
    int IndexSeeks,
    int IndexScans,
    int TableScans,
    int KeyLookups,
    int Sorts,
    int Spills,
    long RequestedMemoryKb,
    long GrantedMemoryKb,
    double MaxEstimateActualRatio,
    string ReadsByTable,
    string UsedIndexes,
    string MissingIndexes);

internal sealed record CreatorAbPreflightEvidence(
    string Database,
    bool Encrypted,
    int Inspections,
    int Checklists,
    int Rfis,
    int InvalidOwners,
    string LatestMigration,
    bool ExperimentalIndexExists);

internal sealed record CreatorAbPhaseEvidence(
    string Name,
    IReadOnlyList<RoleResult> Results,
    IReadOnlyList<PlanEvidence> Plans,
    bool CorrectnessPassed);

internal sealed class CreatorAbEvidenceDocument
{
    public required string RunId { get; init; }
    public required string Database { get; init; }
    public required string ExperimentalIndex { get; init; }
    public required string Status { get; set; }
    public int SamplesPerRole { get; init; }
    public int Concurrency { get; init; }
    public int WarmupsPerRole { get; init; }
    public double AvailableMemoryMiB { get; set; }
    public CreatorAbPreflightEvidence? Preflight { get; set; }
    public List<CreatorAbPhaseEvidence> Phases { get; } = [];
    public bool ExperimentalIndexUsed { get; set; }
    public string? ProductionLikeGate { get; set; }
    public string? LocalStressGate { get; set; }
    public string? Cleanup { get; set; }
    public string? FailureType { get; set; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MemoryStatusEx
{
    public uint Length;
    public uint MemoryLoad;
    public ulong TotalPhysicalMemory;
    public ulong AvailablePhysicalMemory;
    public ulong TotalPageFile;
    public ulong AvailablePageFile;
    public ulong TotalVirtual;
    public ulong AvailableVirtual;
    public ulong AvailableExtendedVirtual;

    public MemoryStatusEx()
    {
        Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
    }
}
