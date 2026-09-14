using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.ConstructionObjects.Commands.CreateConstructionObject;
using ConstructionInspection.Application.ConstructionObjects.Commands.DeactivateConstructionObject;
using ConstructionInspection.Application.ConstructionObjects.Commands.UpdateConstructionObject;
using ConstructionInspection.Application.ConstructionObjects.Models;
using ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjectDetail;
using ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjects;
using ConstructionInspection.Domain.ConstructionObjects;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/construction-objects")]
public sealed class ConstructionObjectsController(ISender sender) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    [ProducesResponseType<ConstructionObjectDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ConstructionObjectDetailDto>> Create(
        CreateConstructionObjectRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateConstructionObjectCommand(
            request.ParentId,
            request.Code,
            request.Name,
            request.ObjectType,
            request.Location,
            request.Description), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<ConstructionObjectListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ConstructionObjectListItemDto>> Get(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] ConstructionObjectType? objectType = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? parentId = null,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortDirection = "asc",
        CancellationToken cancellationToken = default)
    {
        var parentFilterSpecified = Request.Query.ContainsKey("parentId");
        var parentFilterMode = parentFilterSpecified
            ? string.IsNullOrWhiteSpace(parentId)
                ? ParentFilterMode.Roots
                : ParentFilterMode.Specific
            : ParentFilterMode.All;
        Guid? parsedParentId = null;
        if (parentFilterMode == ParentFilterMode.Specific)
        {
            if (!Guid.TryParse(parentId, out var value))
            {
                throw new ValidationException(
                    [new ValidationFailure("parentId", "parentId must be a valid GUID or empty for roots.")]);
            }

            parsedParentId = value;
        }

        return sender.Send(new GetConstructionObjectsQuery(
            pageNumber,
            pageSize,
            search,
            objectType,
            isActive,
            parentFilterMode,
            parsedParentId,
            sortBy,
            sortDirection), cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConstructionObjectDetailDto>(StatusCodes.Status200OK)]
    public Task<ConstructionObjectDetailDto> GetById(
        Guid id,
        CancellationToken cancellationToken) =>
        sender.Send(new GetConstructionObjectDetailQuery(id), cancellationToken);

    [HttpPut("{id:guid}")]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    [ProducesResponseType<ConstructionObjectDetailDto>(StatusCodes.Status200OK)]
    public Task<ConstructionObjectDetailDto> Update(
        Guid id,
        UpdateConstructionObjectRequest request,
        CancellationToken cancellationToken) =>
        sender.Send(new UpdateConstructionObjectCommand(
            id,
            request.ParentId,
            request.Code,
            request.Name,
            request.ObjectType,
            request.Location,
            request.Description,
            request.RowVersion), cancellationToken);

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    [ProducesResponseType<DeactivatedConstructionObjectDto>(StatusCodes.Status200OK)]
    public Task<DeactivatedConstructionObjectDto> Deactivate(
        Guid id,
        DeactivateConstructionObjectRequest request,
        CancellationToken cancellationToken) =>
        sender.Send(
            new DeactivateConstructionObjectCommand(id, request.RowVersion),
            cancellationToken);
}

public sealed record CreateConstructionObjectRequest(
    Guid? ParentId,
    string Code,
    string Name,
    ConstructionObjectType ObjectType,
    string? Location,
    string? Description);

public sealed record UpdateConstructionObjectRequest(
    Guid? ParentId,
    string Code,
    string Name,
    ConstructionObjectType ObjectType,
    string? Location,
    string? Description,
    string RowVersion);

public sealed record DeactivateConstructionObjectRequest(string RowVersion);
