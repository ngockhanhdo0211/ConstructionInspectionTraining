using ConstructionInspection.Application.ConstructionObjects.Models;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjectDetail;

public sealed record GetConstructionObjectDetailQuery(Guid Id)
    : IRequest<ConstructionObjectDetailDto>;
