using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.FormTemplates.Interfaces;
using ConstructionInspection.Application.FormTemplates.Models;
using ConstructionInspection.Domain.FormTemplates;
using MediatR;

namespace ConstructionInspection.Application.FormTemplates.Queries;

public sealed record GetFormTemplatesQuery(
    int PageNumber = 1, int PageSize = 20, string? Search = null, FormTemplateStatus? Status = null,
    string SortBy = "name", string SortDirection = "asc") : IRequest<PagedResult<FormTemplateListItemDto>>;
public sealed record GetFormTemplateVersionsQuery(
    Guid Id, int PageNumber = 1, int PageSize = 20, string SortBy = "versionNumber", string SortDirection = "desc")
    : IRequest<FormTemplateVersionsDto>;
public sealed record GetFormTemplateVersionDetailQuery(Guid Id, int VersionNumber)
    : IRequest<FormTemplateVersionDetailDto>;

public sealed class GetFormTemplatesQueryHandler(ICurrentUser currentUser, IFormTemplateReadService readService)
    : IRequestHandler<GetFormTemplatesQuery, PagedResult<FormTemplateListItemDto>>
{
    public Task<PagedResult<FormTemplateListItemDto>> Handle(GetFormTemplatesQuery request, CancellationToken cancellationToken)
    {
        FormTemplateAuthorization.RequireAdministrator(currentUser);
        return readService.GetTemplatesAsync(new(request.PageNumber, request.PageSize, request.Search,
            request.Status, request.SortBy, request.SortDirection), cancellationToken);
    }
}

public sealed class GetFormTemplateVersionsQueryHandler(ICurrentUser currentUser, IFormTemplateReadService readService)
    : IRequestHandler<GetFormTemplateVersionsQuery, FormTemplateVersionsDto>
{
    public async Task<FormTemplateVersionsDto> Handle(GetFormTemplateVersionsQuery request, CancellationToken cancellationToken)
    {
        FormTemplateAuthorization.RequireAdministrator(currentUser);
        return await readService.GetVersionsAsync(request.Id,
            new(request.PageNumber, request.PageSize, request.SortBy, request.SortDirection), cancellationToken)
            ?? throw new Common.Exceptions.ResourceNotFoundException("form_template.not_found", "The Form Template was not found.");
    }
}

public sealed class GetFormTemplateVersionDetailQueryHandler(ICurrentUser currentUser, IFormTemplateReadService readService)
    : IRequestHandler<GetFormTemplateVersionDetailQuery, FormTemplateVersionDetailDto>
{
    public async Task<FormTemplateVersionDetailDto> Handle(GetFormTemplateVersionDetailQuery request, CancellationToken cancellationToken)
    {
        FormTemplateAuthorization.RequireAdministrator(currentUser);
        return await readService.GetVersionDetailAsync(request.Id, request.VersionNumber, cancellationToken)
            ?? throw new Common.Exceptions.ResourceNotFoundException("form_template.version_not_found", "The Form Template version was not found.");
    }
}
