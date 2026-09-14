using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Models;
using MediatR;

namespace ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjectDetail;

public sealed class GetConstructionObjectDetailQueryHandler(
    ICurrentUser currentUser,
    IConstructionObjectReadService readService)
    : IRequestHandler<GetConstructionObjectDetailQuery, ConstructionObjectDetailDto>
{
    public async Task<ConstructionObjectDetailDto> Handle(
        GetConstructionObjectDetailQuery request,
        CancellationToken cancellationToken)
    {
        var includeInactive = ConstructionObjectAuthorization.CanReadInactive(currentUser);
        return await readService.GetDetailAsync(request.Id, includeInactive, cancellationToken)
            ?? throw new ResourceNotFoundException(
                "construction_object.not_found",
                "The Construction Object was not found.");
    }
}
