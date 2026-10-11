using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.ConstructionObjects;
using ConstructionInspection.Application.ConstructionObjects.Commands;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.DrawingMappings;
using FluentValidation;
using MediatR;

namespace ConstructionInspection.Application.DrawingMappings.Commands.CreateConstructionObjectWithDrawingMapping;

public sealed record CreateConstructionObjectWithDrawingMappingCommand(
    Guid DrawingId, int PageNumber, DrawingMappingShapeType ShapeType, DrawingRectangleDto Geometry,
    Guid? ParentId, string Code, string Name, ConstructionObjectType ObjectType,
    string? Location, string? Description) : IRequest<ConstructionObjectWithDrawingMappingDto>;

public sealed class CreateConstructionObjectWithDrawingMappingCommandValidator
    : AbstractValidator<CreateConstructionObjectWithDrawingMappingCommand>
{
    public CreateConstructionObjectWithDrawingMappingCommandValidator()
    {
        RuleFor(x => x.DrawingId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.ShapeType).Equal(DrawingMappingShapeType.Rectangle);
        RuleFor(x => x.Geometry).NotNull().Must(Commands.DrawingMappingValidation.IsValidGeometry)
            .WithMessage("Geometry must satisfy 0 <= Left < Right <= 1 and 0 <= Bottom < Top <= 1 after rounding to 6 decimals.");
        ConstructionObjectCommandValidation.AddCommonRules(this, x => x.Code, x => x.Name,
            x => x.ObjectType, x => x.Location, x => x.Description);
    }
}

public sealed class CreateConstructionObjectWithDrawingMappingCommandHandler(
    ICurrentUser currentUser, IClock clock,
    IConstructionObjectRepository objectRepository, IConstructionObjectReadService objectReadService,
    IDrawingObjectMappingRepository mappingRepository, IDrawingObjectMappingReadService mappingReadService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateConstructionObjectWithDrawingMappingCommand, ConstructionObjectWithDrawingMappingDto>
{
    public async Task<ConstructionObjectWithDrawingMappingDto> Handle(
        CreateConstructionObjectWithDrawingMappingCommand request, CancellationToken ct)
    {
        var actorId = DrawingMappingAuthorization.RequireAdministrator(currentUser);
        var rectangle = DrawingRectangle.Create(request.Geometry.Left, request.Geometry.Bottom,
            request.Geometry.Right, request.Geometry.Top);
        var ids = await unitOfWork.ExecuteInTransactionAsync(async txCt =>
        {
            await DrawingMappingChecks.RequireReadyDrawingAsync(mappingRepository,
                request.DrawingId, request.PageNumber, txCt);
            var normalizedCode = ConstructionObjectRules.NormalizeCode(request.Code);
            if (await objectRepository.CodeExistsAsync(normalizedCode, null, txCt))
                throw new ConflictException("construction_object.code_duplicate",
                    "A Construction Object with this Code already exists.");
            await ConstructionObjectHierarchyValidator.ValidateAsync(null, request.ObjectType,
                request.ParentId, objectRepository, txCt);

            var objectEntity = ConstructionObject.Create(Guid.NewGuid(), request.ParentId, normalizedCode,
                request.Name, request.ObjectType, request.Location, request.Description, actorId, clock.UtcNow);
            objectRepository.Add(objectEntity);

            await DrawingMappingChecks.EnsureCreateAllowedAsync(mappingRepository, request.DrawingId,
                request.PageNumber, objectEntity.Id, rectangle, txCt);
            var mapping = DrawingObjectMapping.Create(Guid.NewGuid(), request.DrawingId, request.PageNumber,
                objectEntity.Id, request.ShapeType, rectangle, actorId, clock.UtcNow);
            mappingRepository.Add(mapping);
            await unitOfWork.SaveChangesAsync(txCt);
            return (ObjectId: objectEntity.Id, MappingId: mapping.Id);
        }, ct);

        var constructionObject = await objectReadService.GetDetailAsync(ids.ObjectId, true, ct)
            ?? throw new ResourceNotFoundException("construction_object.not_found", "The created Construction Object could not be read.");
        var mapping = await mappingReadService.GetAsync(ids.MappingId, ct)
            ?? throw new ResourceNotFoundException("drawing_mapping.not_found", "The created Drawing mapping could not be read.");
        return new ConstructionObjectWithDrawingMappingDto(constructionObject, mapping);
    }
}
