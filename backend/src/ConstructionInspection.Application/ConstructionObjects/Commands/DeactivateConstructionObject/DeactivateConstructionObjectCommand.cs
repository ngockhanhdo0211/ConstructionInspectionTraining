using ConstructionInspection.Application.ConstructionObjects.Models;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.DeactivateConstructionObject;

public sealed record DeactivateConstructionObjectCommand(Guid Id, string RowVersion)
    : IRequest<DeactivatedConstructionObjectDto>;
