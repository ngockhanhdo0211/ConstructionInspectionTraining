using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Models;
using FluentValidation;
using MediatR;

namespace ConstructionInspection.Application.DrawingMappings.Commands.DeactivateDrawingObjectMapping;

public sealed record DeactivateDrawingObjectMappingCommand(Guid Id, string RowVersion)
    : IRequest<DeactivatedDrawingObjectMappingDto>;

public sealed class DeactivateDrawingObjectMappingCommandValidator : AbstractValidator<DeactivateDrawingObjectMappingCommand>
{
    public DeactivateDrawingObjectMappingCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RowVersion).Must(DrawingMappingRowVersion.IsValid)
            .WithMessage("rowVersion must be valid Base64.");
    }
}

public sealed class DeactivateDrawingObjectMappingCommandHandler(ICurrentUser currentUser, IClock clock,
    IDrawingObjectMappingRepository repository, IUnitOfWork unitOfWork)
    : IRequestHandler<DeactivateDrawingObjectMappingCommand, DeactivatedDrawingObjectMappingDto>
{
    public Task<DeactivatedDrawingObjectMappingDto> Handle(DeactivateDrawingObjectMappingCommand request, CancellationToken ct)
    {
        var actorId = DrawingMappingAuthorization.RequireAdministrator(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async txCt =>
        {
            var entity = await repository.GetForUpdateAsync(request.Id, txCt)
                ?? throw new ResourceNotFoundException("drawing_mapping.not_found", "The Drawing mapping was not found.");
            DrawingMappingRowVersion.EnsureMatches(entity, request.RowVersion);
            entity.Deactivate(actorId, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(txCt);
            return new DeactivatedDrawingObjectMappingDto(entity.Id, entity.Status,
                entity.DeactivatedAt!.Value, entity.DeactivatedByUserId!.Value,
                Convert.ToBase64String(entity.RowVersion));
        }, ct);
    }
}
