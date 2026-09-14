using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.DeactivateConstructionObject;

public sealed class DeactivateConstructionObjectCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IConstructionObjectRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeactivateConstructionObjectCommand, DeactivatedConstructionObjectDto>
{
    public Task<DeactivatedConstructionObjectDto> Handle(
        DeactivateConstructionObjectCommand request,
        CancellationToken cancellationToken)
    {
        var actorId = ConstructionObjectAuthorization.RequireAdministrator(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var entity = await repository.GetForUpdateAsync(request.Id, ct)
                ?? throw new ResourceNotFoundException(
                    "construction_object.not_found",
                    "The Construction Object was not found.");
            ConstructionObjectRowVersion.EnsureMatches(entity, request.RowVersion);

            if (await repository.HasActiveDescendantsAsync(entity.Id, ct))
            {
                throw new ConflictException(
                    "construction_object.active_descendants_exist",
                    "Deactivate all active descendants before deactivating this Construction Object.");
            }

            entity.Deactivate(actorId, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);
            return new DeactivatedConstructionObjectDto(
                entity.Id,
                entity.IsActive,
                entity.UpdatedByUserId!.Value,
                entity.UpdatedAt!.Value,
                Convert.ToBase64String(entity.RowVersion));
        }, cancellationToken);
    }
}
