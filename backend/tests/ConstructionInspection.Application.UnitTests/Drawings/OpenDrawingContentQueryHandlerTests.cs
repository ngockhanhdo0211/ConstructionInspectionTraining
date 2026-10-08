using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Application.Drawings.Models;
using ConstructionInspection.Application.Drawings.Queries.OpenDrawingContent;
using ConstructionInspection.Domain.Drawings;

namespace ConstructionInspection.Application.UnitTests.Drawings;

public sealed class OpenDrawingContentQueryHandlerTests
{
    private static readonly Guid DrawingId = Guid.NewGuid();

    [Fact]
    public async Task ReadyDrawing_ForAdministrator_ReturnsInternalDescriptor()
    {
        var expected = Descriptor(DrawingDocumentStatus.Ready);
        var result = await Handler(ApplicationRoles.Administrator, expected)
            .Handle(new OpenDrawingContentQuery(DrawingId), CancellationToken.None);
        Assert.Same(expected, result);
        Assert.Equal("private/object-key", result.StorageKey);
    }

    [Theory]
    [InlineData(ApplicationRoles.Inspector)]
    [InlineData(ApplicationRoles.Reviewer)]
    public async Task UnscopedReader_IsForbidden(string role) =>
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Handler(role, Descriptor(DrawingDocumentStatus.Ready))
            .Handle(new OpenDrawingContentQuery(DrawingId), CancellationToken.None));

    [Theory]
    [InlineData(DrawingDocumentStatus.Uploading)]
    [InlineData(DrawingDocumentStatus.Failed)]
    public async Task DrawingThatIsNotReady_ReturnsConflict(DrawingDocumentStatus status)
    {
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            Handler(ApplicationRoles.Manager, Descriptor(status))
                .Handle(new OpenDrawingContentQuery(DrawingId), CancellationToken.None));
        Assert.Equal("drawing.not_ready", exception.Code);
    }

    [Fact]
    public async Task MissingDrawing_ReturnsNotFound() =>
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => Handler(ApplicationRoles.Manager, null)
            .Handle(new OpenDrawingContentQuery(DrawingId), CancellationToken.None));

    private static OpenDrawingContentQueryHandler Handler(string role, DrawingContentDescriptor? descriptor) =>
        new(new User(role), new Reads(descriptor));

    private static DrawingContentDescriptor Descriptor(DrawingDocumentStatus status) =>
        new(DrawingId, "drawing.pdf", "private/object-key", "application/pdf", 100,
            new string('a', 64), status, new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc));

    private sealed class User(string role) : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
    }

    private sealed class Reads(DrawingContentDescriptor? descriptor) : IDrawingDocumentReadService
    {
        public Task<DrawingContentDescriptor?> GetContentDescriptorAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(descriptor);
        public Task<DrawingDetailDto?> GetDetailAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedResult<DrawingListItemDto>> GetPageAsync(DrawingQueryCriteria criteria, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
