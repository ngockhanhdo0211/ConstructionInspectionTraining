using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Domain.DrawingMappings;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class DrawingObjectMappingRepository(ApplicationDbContext context)
    : IDrawingObjectMappingRepository
{
    public Task<DrawingObjectMapping?> GetForUpdateAsync(Guid id, CancellationToken ct) =>
        context.DrawingObjectMappings
            .FromSqlInterpolated($"SELECT * FROM [DrawingObjectMappings] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
            .SingleOrDefaultAsync(ct);

    public Task<DrawingDocumentState?> GetDrawingStateAsync(Guid id, CancellationToken ct) =>
        context.DrawingDocuments
            .FromSqlInterpolated($"SELECT * FROM [DrawingDocuments] WITH (HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking()
            .Select(x => new DrawingDocumentState(x.Id, x.PageCount, x.Status))
            .SingleOrDefaultAsync(ct);

    public Task<ConstructionObjectState?> GetConstructionObjectStateAsync(Guid id, CancellationToken ct) =>
        context.ConstructionObjects
            .FromSqlInterpolated($"SELECT * FROM [ConstructionObjects] WITH (HOLDLOCK) WHERE [Id] = {id}")
            .AsNoTracking()
            .Select(x => new ConstructionObjectState(x.Id, x.IsActive))
            .SingleOrDefaultAsync(ct);

    public async Task AcquirePageWriteLockAsync(Guid drawingId, int pageNumber, CancellationToken ct)
    {
        var resource = $"DrawingMapping:{drawingId:N}:{pageNumber}";
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($$"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = {{resource}},
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 15000;
                IF @result < 0
                    THROW 51000, 'Could not acquire Drawing mapping page lock.', 1;
                """, ct);
        }
        catch (SqlException exception) when (exception.Number == 51000)
        {
            throw new ConflictException(
                "drawing_mapping.page_busy",
                "The Drawing page is being updated. Retry the operation.");
        }
    }

    public Task<int> CountActiveOnPageAsync(Guid drawingId, int pageNumber, CancellationToken ct) =>
        context.DrawingObjectMappings.CountAsync(x => x.DrawingDocumentId == drawingId
            && x.PageNumber == pageNumber && x.Status == DrawingObjectMappingStatus.Active, ct);

    public Task<bool> ExactActiveExistsAsync(Guid drawingId, int pageNumber, Guid constructionObjectId,
        DrawingRectangle rectangle, Guid? excludingId, CancellationToken ct) =>
        context.DrawingObjectMappings.AnyAsync(x => x.DrawingDocumentId == drawingId
            && x.PageNumber == pageNumber
            && x.ConstructionObjectId == constructionObjectId
            && x.Status == DrawingObjectMappingStatus.Active
            && x.Left == rectangle.Left && x.Bottom == rectangle.Bottom
            && x.Right == rectangle.Right && x.Top == rectangle.Top
            && (!excludingId.HasValue || x.Id != excludingId.Value), ct);

    public void Add(DrawingObjectMapping mapping) => context.DrawingObjectMappings.Add(mapping);
}
