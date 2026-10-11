using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Domain.Drawings;
using FluentValidation.Results;

namespace ConstructionInspection.Application.DrawingMappings;

internal static class DrawingMappingChecks
{
    public const int MaximumActiveMappingsPerPage = 500;

    public static async Task RequireReadyDrawingAsync(IDrawingObjectMappingRepository repository,
        Guid drawingId, int pageNumber, CancellationToken ct)
    {
        var drawing = await repository.GetDrawingStateAsync(drawingId, ct)
            ?? throw new ResourceNotFoundException("drawing.not_found", "The Drawing was not found.");
        if (drawing.Status != DrawingDocumentStatus.Ready)
            throw new ConflictException("drawing.not_ready", "The Drawing is not ready for mapping.");
        if (pageNumber < 1 || pageNumber > drawing.PageCount)
            throw new FluentValidation.ValidationException([
                new ValidationFailure("pageNumber", "pageNumber must be within the Drawing page range.")
            ]);
    }

    public static async Task RequireActiveObjectAsync(IDrawingObjectMappingRepository repository,
        Guid objectId, CancellationToken ct)
    {
        var target = await repository.GetConstructionObjectStateAsync(objectId, ct)
            ?? throw new ResourceNotFoundException("construction_object.not_found", "The Construction Object was not found.");
        if (!target.IsActive)
            throw new ConflictException("construction_object.inactive", "An inactive Construction Object cannot receive a Drawing mapping.");
    }

    public static async Task EnsureCreateAllowedAsync(IDrawingObjectMappingRepository repository,
        Guid drawingId, int pageNumber, Guid objectId, DrawingRectangle rectangle, CancellationToken ct)
    {
        await repository.AcquirePageWriteLockAsync(drawingId, pageNumber, ct);
        if (await repository.CountActiveOnPageAsync(drawingId, pageNumber, ct) >= MaximumActiveMappingsPerPage)
            throw new ConflictException("drawing_mapping.page_capacity_reached", "The Drawing page already has 500 active mappings.");
        if (await repository.ExactActiveExistsAsync(drawingId, pageNumber, objectId, rectangle, null, ct))
            throw new ConflictException("drawing_mapping.duplicate", "An exact active Drawing mapping already exists.");
    }
}
