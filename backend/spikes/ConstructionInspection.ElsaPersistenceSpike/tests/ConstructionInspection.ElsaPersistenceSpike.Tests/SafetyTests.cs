using Microsoft.Data.SqlClient;
using Xunit;

namespace ConstructionInspection.ElsaPersistenceSpike.Tests;

public sealed class SafetyTests
{
    [Fact]
    public void Guard_accepts_only_exact_spike_database()
    {
        var valid = new SqlConnectionStringBuilder
        {
            DataSource = "localhost",
            InitialCatalog = SpikeDatabaseSafety.ExpectedDatabaseName,
            IntegratedSecurity = true
        }.ConnectionString;

        Assert.Equal(valid, SpikeDatabaseSafety.RequireSafeConnectionString(valid));
        Assert.Throws<InvalidOperationException>(() => SpikeDatabaseSafety.RequireSafeConnectionString(null));
        Assert.Throws<InvalidOperationException>(() => SpikeDatabaseSafety.RequireSafeConnectionString(
            valid.Replace(SpikeDatabaseSafety.ExpectedDatabaseName, "ConstructionInspectionDb", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => SpikeDatabaseSafety.RequireSafeConnectionString(
            valid.Replace(SpikeDatabaseSafety.ExpectedDatabaseName, "${DATABASE_NAME}", StringComparison.Ordinal)));
    }

    [Fact]
    public void Contract_has_exactly_the_nine_approved_events()
    {
        Assert.Equal(9, InspectionEventTypes.Known.Count);
        Assert.Contains(InspectionEventTypes.Created, InspectionEventTypes.Known);
        Assert.Contains(InspectionEventTypes.ReviewerReassigned, InspectionEventTypes.Known);
    }

    [Fact]
    public void Payload_contract_excludes_sensitive_business_content()
    {
        var names = typeof(InspectionEventPayload).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(names, x => x.Contains("Jwt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("DataJson", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Schema", StringComparison.OrdinalIgnoreCase) && x != "SchemaVersion");
        Assert.DoesNotContain(names, x => x.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, x => x.Contains("Comment", StringComparison.OrdinalIgnoreCase));
    }
}
