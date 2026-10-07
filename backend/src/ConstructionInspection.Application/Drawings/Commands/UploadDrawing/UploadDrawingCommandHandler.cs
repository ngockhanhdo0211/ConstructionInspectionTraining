using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Drawings.Interfaces;
using ConstructionInspection.Application.Drawings.Models;
using ConstructionInspection.Domain.Drawings;
using MediatR;

namespace ConstructionInspection.Application.Drawings.Commands.UploadDrawing;

public sealed class UploadDrawingCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IUploadStagingStore staging,
    IPdfMetadataReader pdfReader,
    IObjectStorage objectStorage,
    IDrawingDocumentRepository repository,
    IDrawingDocumentReadService readService,
    IUnitOfWork unitOfWork) : IRequestHandler<UploadDrawingCommand, UploadDrawingResponse>
{
    public async Task<UploadDrawingResponse> Handle(UploadDrawingCommand request, CancellationToken cancellationToken)
    {
        var userId = DrawingAuthorization.RequireAdministrator(currentUser);
        StagedUpload? staged = null;
        try
        {
            staged = await staging.StageAsync(request.Content, DrawingDocumentRules.MaximumFileSize, cancellationToken);
            await using var validationStream = await staging.OpenReadAsync(staged.StagingId, cancellationToken);
            await EnsurePdfSignatureAsync(validationStream, cancellationToken);
            validationStream.Position = 0;
            var metadata = await pdfReader.ReadAsync(validationStream, cancellationToken);
            if (metadata.PageCount is <= 0 or > DrawingDocumentRules.MaximumPageCount)
                throw new PayloadTooLargeException($"PDF page count must be between 1 and {DrawingDocumentRules.MaximumPageCount}.");

            var duplicate = await repository.HasSha256Async(staged.Sha256, cancellationToken);
            var id = Guid.NewGuid();
            var now = clock.UtcNow;
            var storageKey = $"drawings/{now:yyyy/MM}/{id:N}/{Guid.NewGuid():N}.pdf";
            var document = DrawingDocument.CreateUploading(id, request.OriginalFileName, storageKey,
                staged.FileSize, staged.Sha256, metadata.PageCount, userId, now);
            repository.Add(document);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            var uploadAttempted = false;
            try
            {
                await using var uploadStream = await staging.OpenReadAsync(staged.StagingId, cancellationToken);
                uploadAttempted = true;
                await objectStorage.UploadAsync(storageKey, uploadStream, staged.FileSize, "application/pdf", cancellationToken);
                document.MarkReady();
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // The storage call may have committed the object before reporting an error.
                // This key is generated uniquely for this upload, so best-effort deletion
                // cannot remove an object that belonged to an earlier Drawing.
                if (uploadAttempted)
                {
                    try { await objectStorage.DeleteAsync(storageKey, CancellationToken.None); } catch { }
                }
                if (document.Status != DrawingDocumentStatus.Failed)
                {
                    document.MarkFailedAfterCompensation();
                    try { await unitOfWork.SaveChangesAsync(CancellationToken.None); } catch { }
                }
                throw;
            }

            var dto = await readService.GetDetailAsync(id, cancellationToken)
                ?? throw new InvalidOperationException("The uploaded Drawing metadata could not be read back.");
            return new UploadDrawingResponse(dto, duplicate);
        }
        finally
        {
            if (staged is not null) await staging.DeleteAsync(staged.StagingId);
        }
    }

    private static async Task EnsurePdfSignatureAsync(Stream stream, CancellationToken cancellationToken)
    {
        var signature = new byte[5];
        var read = await stream.ReadAsync(signature, cancellationToken);
        if (read != signature.Length || !signature.SequenceEqual("%PDF-"u8.ToArray()))
            throw new UnsupportedMediaTypeException("The uploaded file is not a PDF document.");
    }
}
