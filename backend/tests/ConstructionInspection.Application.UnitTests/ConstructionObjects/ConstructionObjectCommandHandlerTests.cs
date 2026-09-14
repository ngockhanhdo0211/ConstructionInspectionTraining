using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.ConstructionObjects.Commands.CreateConstructionObject;
using ConstructionInspection.Application.ConstructionObjects.Commands.DeactivateConstructionObject;
using ConstructionInspection.Application.ConstructionObjects.Commands.UpdateConstructionObject;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;

namespace ConstructionInspection.Application.UnitTests.ConstructionObjects;

public sealed class ConstructionObjectCommandHandlerTests
{
    private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 9, 13, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_WhenCodeExists_ReturnsConflict()
    {
        var repository = new FakeRepository { CodeExists = true };
        var handler = CreateHandler(repository);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateConstructionObjectCommand(null, "project-a", "Project A", ConstructionObjectType.Project, null, null),
            CancellationToken.None));

        Assert.Equal("construction_object.code_duplicate", exception.Code);
    }

    [Fact]
    public async Task Create_WithValidParent_UsesCurrentUserForAudit()
    {
        var parentId = Guid.NewGuid();
        var repository = new FakeRepository();
        repository.Nodes[parentId] = new(parentId, null, ConstructionObjectType.Project, true);

        await CreateHandler(repository).Handle(
            new CreateConstructionObjectCommand(
                parentId, "BLD-A", "Building A", ConstructionObjectType.Building, null, null),
            CancellationToken.None);

        Assert.NotNull(repository.AddedEntity);
        Assert.Equal(AdminId, repository.AddedEntity.CreatedByUserId);
        Assert.Equal(Now, repository.AddedEntity.CreatedAt);
    }

    [Fact]
    public async Task Create_WhenParentDoesNotExist_ReturnsNotFound()
    {
        var handler = CreateHandler(new FakeRepository());

        await Assert.ThrowsAsync<ResourceNotFoundException>(() => handler.Handle(
            new CreateConstructionObjectCommand(Guid.NewGuid(), "BLD-A", "Building A", ConstructionObjectType.Building, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Create_WhenParentIsInactive_ReturnsConflict()
    {
        var parentId = Guid.NewGuid();
        var repository = new FakeRepository();
        repository.Nodes[parentId] = new(parentId, null, ConstructionObjectType.Project, false);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => CreateHandler(repository).Handle(
            new CreateConstructionObjectCommand(parentId, "BLD-A", "Building A", ConstructionObjectType.Building, null, null),
            CancellationToken.None));

        Assert.Equal("construction_object.parent_inactive", exception.Code);
    }

    [Fact]
    public async Task Create_WithInvalidParentType_ReturnsConflict()
    {
        var parentId = Guid.NewGuid();
        var repository = new FakeRepository();
        repository.Nodes[parentId] = new(parentId, null, ConstructionObjectType.Building, true);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => CreateHandler(repository).Handle(
            new CreateConstructionObjectCommand(parentId, "ROOM-A", "Room A", ConstructionObjectType.Room, null, null),
            CancellationToken.None));

        Assert.Equal("construction_object.invalid_parent_type", exception.Code);
    }

    [Fact]
    public async Task Update_WithSelfParent_ReturnsConflict()
    {
        var entity = CreateEntity();
        var repository = new FakeRepository { Entity = entity };
        var exception = await Assert.ThrowsAsync<ConflictException>(() => UpdateHandler(repository).Handle(
            UpdateCommand(entity, entity.Id), CancellationToken.None));

        Assert.Equal("construction_object.self_parent", exception.Code);
    }

    [Fact]
    public async Task Update_WhenCandidateLineageContainsObject_ReturnsCycleConflict()
    {
        var entity = CreateEntity();
        var parentId = Guid.NewGuid();
        var repository = new FakeRepository { Entity = entity };
        repository.Nodes[parentId] = new(parentId, entity.Id, ConstructionObjectType.Floor, true);
        repository.Nodes[entity.Id] = new(entity.Id, null, ConstructionObjectType.Room, true);

        var exception = await Assert.ThrowsAsync<ConflictException>(() => UpdateHandler(repository).Handle(
            UpdateCommand(entity, parentId), CancellationToken.None));

        Assert.Equal("construction_object.cycle_detected", exception.Code);
    }

    [Fact]
    public async Task Update_WhenDepthWouldExceedFive_ReturnsConflict()
    {
        var entity = CreateEntity(ConstructionObjectType.WorkItem);
        var repository = new FakeRepository { Entity = entity };
        Guid? previous = null;
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var types = new[]
        {
            ConstructionObjectType.Project,
            ConstructionObjectType.Project,
            ConstructionObjectType.Building,
            ConstructionObjectType.Floor,
            ConstructionObjectType.Room
        };
        for (var index = 0; index < ids.Length; index++)
        {
            repository.Nodes[ids[index]] = new(ids[index], previous, types[index], true);
            previous = ids[index];
        }

        var exception = await Assert.ThrowsAsync<ConflictException>(() => UpdateHandler(repository).Handle(
            UpdateCommand(entity, ids[^1], ConstructionObjectType.WorkItem), CancellationToken.None));

        Assert.Equal("construction_object.maximum_depth_exceeded", exception.Code);
    }

    [Fact]
    public async Task Update_WhenObjectTypeChanges_ReturnsConflict()
    {
        var entity = CreateEntity();
        var repository = new FakeRepository { Entity = entity };

        var exception = await Assert.ThrowsAsync<ConflictException>(() => UpdateHandler(repository).Handle(
            UpdateCommand(entity, entity.ParentId, ConstructionObjectType.WorkItem),
            CancellationToken.None));

        Assert.Equal("construction_object.object_type_immutable", exception.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deactivate_WhenActiveDirectOrIndirectDescendantExists_ReturnsConflict(
        bool isIndirect)
    {
        var entity = CreateEntity();
        var repository = new FakeRepository
        {
            Entity = entity,
            HasActiveDescendants = true,
            ActiveDescendantIsIndirect = isIndirect
        };

        var exception = await Assert.ThrowsAsync<ConflictException>(() => DeactivateHandler(repository).Handle(
            new DeactivateConstructionObjectCommand(entity.Id, string.Empty), CancellationToken.None));

        Assert.Equal("construction_object.active_descendants_exist", exception.Code);
    }

    [Fact]
    public async Task Deactivate_WhenOnlyInactiveChildrenExist_Succeeds()
    {
        var entity = CreateEntity();
        var repository = new FakeRepository { Entity = entity };

        var result = await DeactivateHandler(repository).Handle(
            new DeactivateConstructionObjectCommand(entity.Id, string.Empty), CancellationToken.None);

        Assert.False(result.IsActive);
        Assert.Equal(AdminId, result.UpdatedByUserId);
    }

    [Fact]
    public async Task Update_WithOldRowVersion_ReturnsConflict()
    {
        var entity = CreateEntity();
        var repository = new FakeRepository { Entity = entity };

        var exception = await Assert.ThrowsAsync<ConflictException>(() => UpdateHandler(repository).Handle(
            UpdateCommand(entity, null) with { RowVersion = "AQ==" }, CancellationToken.None));

        Assert.Equal("construction_object.concurrency_conflict", exception.Code);
    }

    [Fact]
    public async Task Create_WithWrongRole_ReturnsForbidden()
    {
        var handler = CreateHandler(
            new FakeRepository(),
            new FakeCurrentUser(AdminId, [ApplicationRoles.Inspector]));

        await Assert.ThrowsAsync<ForbiddenAccessException>(() => handler.Handle(
            new CreateConstructionObjectCommand(null, "PROJECT-A", "Project A", ConstructionObjectType.Project, null, null),
            CancellationToken.None));
    }

    private static CreateConstructionObjectCommandHandler CreateHandler(
        FakeRepository repository,
        ICurrentUser? currentUser = null) => new(
            currentUser ?? Admin(),
            new FakeClock(),
            repository,
            new FakeReadService(),
            new FakeUnitOfWork());

    private static UpdateConstructionObjectCommandHandler UpdateHandler(FakeRepository repository) => new(
        Admin(), new FakeClock(), repository, new FakeReadService(), new FakeUnitOfWork());

    private static DeactivateConstructionObjectCommandHandler DeactivateHandler(FakeRepository repository) => new(
        Admin(), new FakeClock(), repository, new FakeUnitOfWork());

    private static FakeCurrentUser Admin() => new(AdminId, [ApplicationRoles.Administrator]);

    private static ConstructionObject CreateEntity(
        ConstructionObjectType type = ConstructionObjectType.Room) => ConstructionObject.Create(
            Guid.NewGuid(), null, "OBJECT-A", "Object A", type, null, null, AdminId, Now);

    private static UpdateConstructionObjectCommand UpdateCommand(
        ConstructionObject entity,
        Guid? parentId,
        ConstructionObjectType type = ConstructionObjectType.Room) => new(
            entity.Id, parentId, entity.Code, entity.Name, type, null, null, string.Empty);

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class FakeCurrentUser(Guid? userId, IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public IReadOnlyCollection<string> Roles { get; } = roles;
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) => operation(cancellationToken);
    }

    private sealed class FakeRepository : IConstructionObjectRepository
    {
        public Dictionary<Guid, ConstructionObjectHierarchyNode> Nodes { get; } = [];
        public ConstructionObject? Entity { get; init; }
        public bool CodeExists { get; init; }
        public bool HasActiveDescendants { get; init; }
        public bool ActiveDescendantIsIndirect { get; init; }
        public ConstructionObject? AddedEntity { get; private set; }

        public void Add(ConstructionObject constructionObject)
        {
            AddedEntity = constructionObject;
        }

        public Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludingId, CancellationToken cancellationToken) =>
            Task.FromResult(CodeExists);

        public Task<ConstructionObject?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Entity?.Id == id ? Entity : null);

        public Task<ConstructionObjectHierarchyNode?> GetHierarchyNodeAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Nodes.GetValueOrDefault(id));

        public Task<bool> HasActiveDescendantsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(HasActiveDescendants);
    }

    private sealed class FakeReadService : IConstructionObjectReadService
    {
        public Task<ConstructionObjectDetailDto?> GetDetailAsync(Guid id, bool includeInactive, CancellationToken cancellationToken) =>
            Task.FromResult<ConstructionObjectDetailDto?>(new(
                id, null, "OBJECT-A", "Object A", ConstructionObjectType.Project,
                null, null, true, null, [], false, 0, AdminId, Now, null, null, string.Empty));

        public Task<PagedResult<ConstructionObjectListItemDto>> GetPageAsync(
            ConstructionObjectQueryCriteria criteria,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
