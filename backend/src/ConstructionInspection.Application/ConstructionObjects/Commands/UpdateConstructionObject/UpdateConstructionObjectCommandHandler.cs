using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.UpdateConstructionObject;

public sealed class UpdateConstructionObjectCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IConstructionObjectRepository repository,
    IConstructionObjectReadService readService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateConstructionObjectCommand, ConstructionObjectDetailDto>
{
    public async Task<ConstructionObjectDetailDto> Handle(
        UpdateConstructionObjectCommand request,
        CancellationToken cancellationToken)
    {
        var actorId = ConstructionObjectAuthorization.RequireAdministrator(currentUser);
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var entity = await repository.GetForUpdateAsync(request.Id, ct)
                ?? throw new ResourceNotFoundException(
                    "construction_object.not_found",
                    "The Construction Object was not found.");
            ConstructionObjectRowVersion.EnsureMatches(entity, request.RowVersion);

            if (entity.ObjectType != request.ObjectType)
            {
                throw new ConflictException(
                    "construction_object.object_type_immutable",
                    "ObjectType cannot be changed after creation.");
            }

            var normalizedCode = ConstructionObjectRules.NormalizeCode(request.Code);
            if (await repository.CodeExistsAsync(normalizedCode, entity.Id, ct))
            {
                throw new ConflictException(
                    "construction_object.code_duplicate",
                    "A Construction Object with this Code already exists.");
            }

            await ConstructionObjectHierarchyValidator.ValidateAsync(
                entity.Id,
                request.ObjectType,
                request.ParentId,
                repository,
                ct);

            entity.Update(
                request.ParentId,
                normalizedCode,
                request.Name,
                request.ObjectType,
                request.Location,
                request.Description,
                actorId,
                clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);

        return await readService.GetDetailAsync(request.Id, true, cancellationToken)
            ?? throw new ResourceNotFoundException(
                "construction_object.not_found",
                "The updated Construction Object could not be read.");
    }
}
