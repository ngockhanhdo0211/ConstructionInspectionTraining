using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.UpdateConstructionObject;

public sealed record UpdateConstructionObjectCommand(
    Guid Id,
    Guid? ParentId,
    string Code,
    string Name,
    ConstructionObjectType ObjectType,
    string? Location,
    string? Description,
    string RowVersion) : IRequest<ConstructionObjectDetailDto>;
