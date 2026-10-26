using System.Data;
using ConstructionInspection.Application.Rfis.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class RfiNumberSequence(ApplicationDbContext context) : IRfiNumberSequence
{
    public async Task<long> NextAsync(CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NEXT VALUE FOR [RfiNumberSequence]";
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
