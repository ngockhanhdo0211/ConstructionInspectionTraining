using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Models;
using FluentValidation;
using MediatR;

namespace ConstructionInspection.Application.DrawingMappings.Queries.GetConstructionObjectDrawingMappings;

public sealed record GetConstructionObjectDrawingMappingsQuery(Guid ConstructionObjectId, int PageNumber, int PageSize)
    : IRequest<ConstructionObjectDrawingMappingsDto>;

public sealed class GetConstructionObjectDrawingMappingsQueryValidator
    : AbstractValidator<GetConstructionObjectDrawingMappingsQuery>
{
    public GetConstructionObjectDrawingMappingsQueryValidator()
    {
        RuleFor(x => x.ConstructionObjectId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetConstructionObjectDrawingMappingsQueryHandler(ICurrentUser currentUser,
    IDrawingObjectMappingReadService readService)
    : IRequestHandler<GetConstructionObjectDrawingMappingsQuery, ConstructionObjectDrawingMappingsDto>
{
    public async Task<ConstructionObjectDrawingMappingsDto> Handle(
        GetConstructionObjectDrawingMappingsQuery request, CancellationToken ct)
    {
        DrawingMappingAuthorization.RequireReader(currentUser);
        return await readService.GetSourcesAsync(request.ConstructionObjectId,
                new DrawingMappingQueryCriteria(request.PageNumber, request.PageSize), ct)
            ?? throw new ResourceNotFoundException("construction_object.not_found", "The Construction Object was not found.");
    }
}
