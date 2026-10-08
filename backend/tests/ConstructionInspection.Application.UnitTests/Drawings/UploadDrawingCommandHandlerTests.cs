using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Drawings.Commands.UploadDrawing;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Application.Drawings.Models;
using ConstructionInspection.Domain.Drawings;

namespace ConstructionInspection.Application.UnitTests.Drawings;

public sealed class UploadDrawingCommandHandlerTests
{
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Upload_WhenShaAlreadyExists_AllowsUploadAndReturnsWarning()
    {
        var state = new State { Duplicate = true };
        var result = await Handler(state).Handle(Command(), CancellationToken.None);
        Assert.True(result.DuplicateWarning);
        Assert.True(state.Uploaded);
        Assert.Equal(DrawingDocumentStatus.Ready, state.Document!.Status);
    }

    [Fact]
    public async Task Upload_WhenStorageFails_MarksMetadataFailed()
    {
        var state = new State { StorageFailure = new IOException("storage unavailable") };
        await Assert.ThrowsAsync<IOException>(() => Handler(state).Handle(Command(), CancellationToken.None));
        Assert.Equal(DrawingDocumentStatus.Failed, state.Document!.Status);
        Assert.True(state.Deleted);
        Assert.Equal(2, state.SaveCount);
    }

    [Fact]
    public async Task Upload_WhenFinalDatabaseSaveFails_DeletesUploadedObject()
    {
        var state = new State { FailSaveNumber = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(state).Handle(Command(), CancellationToken.None));
        Assert.True(state.Deleted);
        Assert.Equal(DrawingDocumentStatus.Failed, state.Document!.Status);
    }

    [Fact]
    public async Task Upload_WithNonAdministrator_IsForbidden()
    {
        var state = new State();
        await Assert.ThrowsAsync<ForbiddenAccessException>(() => Handler(state, ApplicationRoles.Manager)
            .Handle(Command(), CancellationToken.None));
        Assert.Null(state.Document);
    }

    private static UploadDrawingCommand Command() => new("plan.pdf", new MemoryStream("%PDF-test"u8.ToArray()));

    private static UploadDrawingCommandHandler Handler(State state, string role = ApplicationRoles.Administrator) => new(
        new User(role), new Clock(), new Staging(), new Pdf(), new Storage(state), new Repository(state),
        new Reads(state), new UnitOfWork(state));

    private sealed class State
    {
        public bool Duplicate { get; init; }
        public Exception? StorageFailure { get; init; }
        public int FailSaveNumber { get; init; }
        public int SaveCount { get; set; }
        public bool Uploaded { get; set; }
        public bool Deleted { get; set; }
        public DrawingDocument? Document { get; set; }
    }
    private sealed class User(string role) : ICurrentUser
    {
        public Guid? UserId => AdminId;
        public IReadOnlyCollection<string> Roles => [role];
        public bool IsInRole(string value) => value == role;
    }
    private sealed class Clock : IClock { public DateTime UtcNow => Now; }
    private sealed class Staging : IUploadStagingStore
    {
        public Task<StagedUpload> StageAsync(Stream c, long m, CancellationToken ct) =>
            Task.FromResult(new StagedUpload("id", 9, new string('a', 64)));
        public Task<Stream> OpenReadAsync(string id, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream("%PDF-test"u8.ToArray()));
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }
    private sealed class Pdf : IPdfMetadataReader
    {
        public Task<PdfMetadata> ReadAsync(Stream c, CancellationToken ct) => Task.FromResult(new PdfMetadata(2));
    }
    private sealed class Storage(State s) : IObjectStorage
    {
        public Task UploadAsync(string k, Stream c, long l, string t, CancellationToken ct)
        {
            if (s.StorageFailure is not null) throw s.StorageFailure;
            s.Uploaded = true; return Task.CompletedTask;
        }
        public Task CopyToAsync(string k, Stream d, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string k, CancellationToken ct) { s.Deleted = true; return Task.CompletedTask; }
        public Task<bool> ExistsAsync(string k, CancellationToken ct) => Task.FromResult(false);
        public Task<ObjectStorageMetadata?> GetMetadataAsync(string k, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task CopyRangeToAsync(string k, Stream d, long o, long l, CancellationToken ct) =>
            throw new NotSupportedException();
    }
    private sealed class Repository(State s) : IDrawingDocumentRepository
    {
        public void Add(DrawingDocument d) => s.Document = d;
        public Task<bool> HasSha256Async(string h, CancellationToken ct) => Task.FromResult(s.Duplicate);
    }
    private sealed class Reads(State s) : IDrawingDocumentReadService
    {
        public Task<DrawingDetailDto?> GetDetailAsync(Guid id, CancellationToken ct)
        {
            var d = s.Document!;
            return Task.FromResult<DrawingDetailDto?>(new(d.Id, d.OriginalFileName, d.ContentType, d.FileSize,
                d.Sha256, d.PageCount, d.Status, d.CreatedAt, d.CreatedByUserId, ""));
        }
        public Task<PagedResult<DrawingListItemDto>> GetPageAsync(DrawingQueryCriteria c, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<DrawingContentDescriptor?> GetContentDescriptorAsync(Guid id, CancellationToken ct) =>
            throw new NotSupportedException();
    }
    private sealed class UnitOfWork(State s) : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct)
        {
            s.SaveCount++;
            if (s.SaveCount == s.FailSaveNumber) throw new InvalidOperationException("database failure");
            return Task.CompletedTask;
        }
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct) => operation(ct);
    }
}
