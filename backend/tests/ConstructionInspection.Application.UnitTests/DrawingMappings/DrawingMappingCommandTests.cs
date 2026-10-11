using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Commands.CreateDrawingObjectMapping;
using ConstructionInspection.Application.DrawingMappings.Commands.UpdateDrawingObjectMapping;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Models;
using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Domain.Drawings;

namespace ConstructionInspection.Application.UnitTests.DrawingMappings;

public sealed class DrawingMappingCommandTests
{
    [Fact]
    public async Task Create_NormalizesGeometryAndPersistsMapping()
    {
        var repository = new Repository();
        var handler = Handler(repository);

        var result = await handler.Handle(Command(0.12345649m, 0.98765451m), CancellationToken.None);

        Assert.NotNull(repository.Added);
        Assert.Equal(0.123456m, repository.Added.Left);
        Assert.Equal(0.987655m, repository.Added.Right);
        Assert.Equal(repository.Added.Id, result.Id);
    }

    [Fact]
    public async Task Create_WithInactiveObject_ReturnsConflict()
    {
        var repository = new Repository { ObjectIsActive = false };
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            Handler(repository).Handle(Command(), CancellationToken.None));
        Assert.Equal("construction_object.inactive", error.Code);
    }

    [Fact]
    public async Task Create_AtCapacity_ReturnsStableConflict()
    {
        var repository = new Repository { ActiveCount = 500 };
        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            Handler(repository).Handle(Command(), CancellationToken.None));
        Assert.Equal("drawing_mapping.page_capacity_reached", error.Code);
        Assert.True(repository.LockAcquired);
    }

    [Fact]
    public void UpdateValidator_RequiresRowVersionAndValidGeometry()
    {
        var validator = new UpdateDrawingObjectMappingCommandValidator();
        var result = validator.Validate(new UpdateDrawingObjectMappingCommand(Guid.NewGuid(), Guid.NewGuid(),
            new DrawingRectangleDto(0.2m, 0.2m, 0.2m, 0.4m), string.Empty));
        Assert.Contains(result.Errors, x => x.PropertyName == "Geometry");
        Assert.Contains(result.Errors, x => x.PropertyName == "RowVersion");
    }

    private static CreateDrawingObjectMappingCommandHandler Handler(Repository repository) =>
        new(new User(), new Clock(), repository, new Reads(repository), new UnitOfWork());

    private static CreateDrawingObjectMappingCommand Command(decimal left = 0.1m, decimal right = 0.5m) =>
        new(Repository.DrawingId, 1, Repository.ObjectId, DrawingMappingShapeType.Rectangle,
            new DrawingRectangleDto(left, 0.2m, right, 0.8m));

    private sealed class User : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [ApplicationRoles.Administrator];
        public bool IsInRole(string role) => role == ApplicationRoles.Administrator;
    }

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) => operation(ct);
    }

    private sealed class Repository : IDrawingObjectMappingRepository
    {
        public static readonly Guid DrawingId = Guid.NewGuid();
        public static readonly Guid ObjectId = Guid.NewGuid();
        public bool ObjectIsActive { get; init; } = true;
        public int ActiveCount { get; init; }
        public bool LockAcquired { get; private set; }
        public DrawingObjectMapping? Added { get; private set; }
        public Task<DrawingObjectMapping?> GetForUpdateAsync(Guid id, CancellationToken ct) => Task.FromResult<DrawingObjectMapping?>(null);
        public Task<DrawingDocumentState?> GetDrawingStateAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<DrawingDocumentState?>(new(id, 2, DrawingDocumentStatus.Ready));
        public Task<ConstructionObjectState?> GetConstructionObjectStateAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ConstructionObjectState?>(new(id, ObjectIsActive));
        public Task AcquirePageWriteLockAsync(Guid drawingId, int pageNumber, CancellationToken ct)
        { LockAcquired = true; return Task.CompletedTask; }
        public Task<int> CountActiveOnPageAsync(Guid drawingId, int pageNumber, CancellationToken ct) => Task.FromResult(ActiveCount);
        public Task<bool> ExactActiveExistsAsync(Guid drawingId, int pageNumber, Guid constructionObjectId,
            DrawingRectangle rectangle, Guid? excludingId, CancellationToken ct) => Task.FromResult(false);
        public void Add(DrawingObjectMapping mapping) => Added = mapping;
    }

    private sealed class Reads(Repository repository) : IDrawingObjectMappingReadService
    {
        public Task<DrawingObjectMappingDto?> GetAsync(Guid id, CancellationToken ct)
        {
            var x = repository.Added!;
            return Task.FromResult<DrawingObjectMappingDto?>(new(x.Id, x.DrawingDocumentId, x.PageNumber,
                new MappingConstructionObjectDto(x.ConstructionObjectId, "P-1", "Project", ConstructionInspection.Domain.ConstructionObjects.ConstructionObjectType.Project, true),
                x.ShapeType, new DrawingRectangleDto(x.Left, x.Bottom, x.Right, x.Top), x.Status,
                x.CreatedAt, x.CreatedByUserId, null, null, null, null, string.Empty));
        }
        public Task<DrawingPageMappingsDto?> GetPageAsync(Guid drawingId, int pageNumber, CancellationToken ct) => throw new NotSupportedException();
        public Task<ConstructionObjectDrawingMappingsDto?> GetSourcesAsync(Guid constructionObjectId,
            DrawingMappingQueryCriteria criteria, CancellationToken ct) => throw new NotSupportedException();
    }
}
