using ConstructionInspection.Application.Drawings.Models;
using MediatR;

namespace ConstructionInspection.Application.Drawings.Queries.GetDrawingDetail;

public sealed record GetDrawingDetailQuery(Guid Id) : IRequest<DrawingDetailDto>;
