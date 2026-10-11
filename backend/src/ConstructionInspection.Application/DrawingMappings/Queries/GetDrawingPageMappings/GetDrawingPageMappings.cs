using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Interfaces;
using ConstructionInspection.Application.DrawingMappings.Models;
using FluentValidation;
using MediatR;

namespace ConstructionInspection.Application.DrawingMappings.Queries.GetDrawingPageMappings;

public sealed record GetDrawingPageMappingsQuery(Guid DrawingId, int PageNumber) : IRequest<DrawingPageMappingsDto>;

public sealed class GetDrawingPageMappingsQueryValidator : AbstractValidator<GetDrawingPageMappingsQuery>
{
    public GetDrawingPageMappingsQueryValidator()
    {
        RuleFor(x => x.DrawingId).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
    }
}

public sealed class GetDrawingPageMappingsQueryHandler(ICurrentUser currentUser,
    IDrawingObjectMappingRepository repository, IDrawingObjectMappingReadService readService)
    : IRequestHandler<GetDrawingPageMappingsQuery, DrawingPageMappingsDto>
{
    public async Task<DrawingPageMappingsDto> Handle(GetDrawingPageMappingsQuery request, CancellationToken ct)
    {
        DrawingMappingAuthorization.RequireReader(currentUser);
        await DrawingMappingChecks.RequireReadyDrawingAsync(repository, request.DrawingId, request.PageNumber, ct);
        return await readService.GetPageAsync(request.DrawingId, request.PageNumber, ct)
            ?? throw new ResourceNotFoundException("drawing.not_found", "The Drawing was not found.");
    }
}
