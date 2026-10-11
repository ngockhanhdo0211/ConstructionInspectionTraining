using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Models;
using ConstructionInspection.Domain.DrawingMappings;
using FluentValidation;
using MediatR;

namespace ConstructionInspection.Application.DrawingMappings.Commands.UpdateDrawingObjectMapping;

public sealed record UpdateDrawingObjectMappingCommand(Guid Id, Guid ConstructionObjectId,
    DrawingRectangleDto Geometry, string RowVersion) : IRequest<DrawingObjectMappingDto>;

public sealed class UpdateDrawingObjectMappingCommandValidator : AbstractValidator<UpdateDrawingObjectMappingCommand>
{
    public UpdateDrawingObjectMappingCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ConstructionObjectId).NotEmpty();
        RuleFor(x => x.Geometry).NotNull().Must(Commands.DrawingMappingValidation.IsValidGeometry)
            .WithMessage("Geometry must satisfy 0 <= Left < Right <= 1 and 0 <= Bottom < Top <= 1 after rounding to 6 decimals.");
        RuleFor(x => x.RowVersion).Must(DrawingMappingRowVersion.IsValid)
            .WithMessage("rowVersion must be valid Base64.");
    }
}

public sealed class UpdateDrawingObjectMappingCommandHandler(ICurrentUser currentUser, IClock clock,
    IDrawingObjectMappingRepository repository, IDrawingObjectMappingReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateDrawingObjectMappingCommand, DrawingObjectMappingDto>
{
    public async Task<DrawingObjectMappingDto> Handle(UpdateDrawingObjectMappingCommand request, CancellationToken ct)
    {
        var actorId = DrawingMappingAuthorization.RequireAdministrator(currentUser);
        var rectangle = DrawingRectangle.Create(request.Geometry.Left, request.Geometry.Bottom,
            request.Geometry.Right, request.Geometry.Top);
        await unitOfWork.ExecuteInTransactionAsync(async txCt =>
        {
            var entity = await repository.GetForUpdateAsync(request.Id, txCt)
                ?? throw new ResourceNotFoundException("drawing_mapping.not_found", "The Drawing mapping was not found.");
            DrawingMappingRowVersion.EnsureMatches(entity, request.RowVersion);
            await DrawingMappingChecks.RequireReadyDrawingAsync(repository, entity.DrawingDocumentId, entity.PageNumber, txCt);
            await DrawingMappingChecks.RequireActiveObjectAsync(repository, request.ConstructionObjectId, txCt);
            if (await repository.ExactActiveExistsAsync(entity.DrawingDocumentId, entity.PageNumber,
                    request.ConstructionObjectId, rectangle, entity.Id, txCt))
                throw new ConflictException("drawing_mapping.duplicate", "An exact active Drawing mapping already exists.");
            entity.Update(request.ConstructionObjectId, rectangle, actorId, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(txCt);
            return true;
        }, ct);

        return await readService.GetAsync(request.Id, ct)
            ?? throw new ResourceNotFoundException("drawing_mapping.not_found", "The updated Drawing mapping could not be read.");
    }
}
