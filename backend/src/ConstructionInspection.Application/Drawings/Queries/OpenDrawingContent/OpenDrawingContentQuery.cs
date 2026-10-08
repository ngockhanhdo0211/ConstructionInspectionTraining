using ConstructionInspection.Application.Drawings.Models;
using MediatR;

namespace ConstructionInspection.Application.Drawings.Queries.OpenDrawingContent;

public sealed record OpenDrawingContentQuery(Guid Id) : IRequest<DrawingContentDescriptor>;
