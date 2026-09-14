using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Domain.ConstructionObjects;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.CreateConstructionObject;

public sealed record CreateConstructionObjectCommand(
    Guid? ParentId,
    string Code,
    string Name,
    ConstructionObjectType ObjectType,
    string? Location,
    string? Description) : IRequest<ConstructionObjectDetailDto>;
