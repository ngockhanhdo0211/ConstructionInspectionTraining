using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Models;
using ConstructionInspection.Domain.DrawingMappings;
using FluentValidation;
using MediatR;

namespace ConstructionInspection.Application.DrawingMappings.Commands.CreateDrawingObjectMapping;

public sealed record CreateDrawingObjectMappingCommand(Guid DrawingId, int PageNumber,
    Guid ConstructionObjectId, DrawingMappingShapeType ShapeType, DrawingRectangleDto Geometry)
    : IRequest<DrawingObjectMappingDto>;

public sealed class CreateDrawingObjectMappingCommandValidator : AbstractValidator<CreateDrawingObjectMappingCommand>
{
    public CreateDrawingObjectMappingCommandValidator()
    {
        RuleFor(x => x.DrawingId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.ConstructionObjectId).NotEmpty();
        RuleFor(x => x.ShapeType).Equal(DrawingMappingShapeType.Rectangle);
        RuleFor(x => x.Geometry).NotNull().Must(DrawingMappingValidation.IsValidGeometry)
            .WithMessage("Geometry must satisfy 0 <= Left < Right <= 1 and 0 <= Bottom < Top <= 1 after rounding to 6 decimals.");
    }
}

public sealed class CreateDrawingObjectMappingCommandHandler(ICurrentUser currentUser, IClock clock,
    IDrawingObjectMappingRepository repository, IDrawingObjectMappingReadService readService, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateDrawingObjectMappingCommand, DrawingObjectMappingDto>
{
    public async Task<DrawingObjectMappingDto> Handle(CreateDrawingObjectMappingCommand request, CancellationToken ct)
    {
        var actorId = DrawingMappingAuthorization.RequireAdministrator(currentUser);
        var rectangle = DrawingRectangle.Create(request.Geometry.Left, request.Geometry.Bottom,
            request.Geometry.Right, request.Geometry.Top);
        var id = await unitOfWork.ExecuteInTransactionAsync(async txCt =>
        {
            await DrawingMappingChecks.RequireReadyDrawingAsync(repository, request.DrawingId, request.PageNumber, txCt);
            await DrawingMappingChecks.RequireActiveObjectAsync(repository, request.ConstructionObjectId, txCt);
            await DrawingMappingChecks.EnsureCreateAllowedAsync(repository, request.DrawingId, request.PageNumber,
                request.ConstructionObjectId, rectangle, txCt);
            var entity = DrawingObjectMapping.Create(Guid.NewGuid(), request.DrawingId, request.PageNumber,
                request.ConstructionObjectId, request.ShapeType, rectangle, actorId, clock.UtcNow);
            repository.Add(entity);
            await unitOfWork.SaveChangesAsync(txCt);
            return entity.Id;
        }, ct);

        return await readService.GetAsync(id, ct)
            ?? throw new ResourceNotFoundException("drawing_mapping.not_found", "The created Drawing mapping could not be read.");
    }
}
