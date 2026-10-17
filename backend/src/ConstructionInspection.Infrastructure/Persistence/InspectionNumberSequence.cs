using System.Data;
using ConstructionInspection.Application.Inspections.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class InspectionNumberSequence(ApplicationDbContext context) : IInspectionNumberSequence
{
    public async Task<long> GetNextAsync(CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var closeWhenFinished = connection.State != ConnectionState.Open;
        if (closeWhenFinished) await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT NEXT VALUE FOR [InspectionNumberSequence]";
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (closeWhenFinished) await connection.CloseAsync();
        }
    }
}
