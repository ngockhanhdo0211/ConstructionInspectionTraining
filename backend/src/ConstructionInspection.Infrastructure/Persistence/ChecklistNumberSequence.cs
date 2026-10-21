using System.Data;
using ConstructionInspection.Application.Checklists.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class ChecklistNumberSequence(ApplicationDbContext context) : IChecklistNumberSequence
{
    public async Task<long> NextAsync(CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NEXT VALUE FOR [ChecklistNumberSequence]";
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
