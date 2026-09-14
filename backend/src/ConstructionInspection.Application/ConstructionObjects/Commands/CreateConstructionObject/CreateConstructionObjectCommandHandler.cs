using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.CreateConstructionObject;

public sealed class CreateConstructionObjectCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IConstructionObjectRepository repository,
    IConstructionObjectReadService readService,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateConstructionObjectCommand, ConstructionObjectDetailDto>
{
    public async Task<ConstructionObjectDetailDto> Handle(
        CreateConstructionObjectCommand request,
        CancellationToken cancellationToken)
    {
        var actorId = ConstructionObjectAuthorization.RequireAdministrator(currentUser);
        var id = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var normalizedCode = ConstructionObjectRules.NormalizeCode(request.Code);
            if (await repository.CodeExistsAsync(normalizedCode, null, ct))
            {
                throw new ConflictException(
                    "construction_object.code_duplicate",
                    "A Construction Object with this Code already exists.");
            }

            await ConstructionObjectHierarchyValidator.ValidateAsync(
                null,
                request.ObjectType,
                request.ParentId,
                repository,
                ct);

            var entity = ConstructionObject.Create(
                Guid.NewGuid(),
                request.ParentId,
                normalizedCode,
                request.Name,
                request.ObjectType,
                request.Location,
                request.Description,
                actorId,
                clock.UtcNow);
            repository.Add(entity);
            await unitOfWork.SaveChangesAsync(ct);
            return entity.Id;
        }, cancellationToken);

        return await readService.GetDetailAsync(id, true, cancellationToken)
            ?? throw new ResourceNotFoundException(
                "construction_object.not_found",
                "The created Construction Object could not be read.");
    }
}
