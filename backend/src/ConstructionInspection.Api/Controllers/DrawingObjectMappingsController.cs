using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.DrawingMappings.Commands.CreateConstructionObjectWithDrawingMapping;
using ConstructionInspection.Application.DrawingMappings.Commands.CreateDrawingObjectMapping;
using ConstructionInspection.Application.DrawingMappings.Commands.DeactivateDrawingObjectMapping;
using ConstructionInspection.Application.DrawingMappings.Commands.UpdateDrawingObjectMapping;
using ConstructionInspection.Application.DrawingMappings.Models;
using ConstructionInspection.Application.DrawingMappings.Queries.GetConstructionObjectDrawingMappings;
using ConstructionInspection.Application.DrawingMappings.Queries.GetDrawingPageMappings;
using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.DrawingMappings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Administrator + "," + ApplicationRoles.Manager)]
public sealed class DrawingObjectMappingsController(ISender sender) : ControllerBase
{
    [HttpGet("api/drawings/{drawingId:guid}/mappings")]
    public Task<DrawingPageMappingsDto> GetPage(Guid drawingId, [FromQuery] int pageNumber,
        CancellationToken ct) => sender.Send(new GetDrawingPageMappingsQuery(drawingId, pageNumber), ct);

    [HttpPost("api/drawings/{drawingId:guid}/mappings")]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    public async Task<ActionResult<DrawingObjectMappingDto>> Create(Guid drawingId,
        CreateDrawingObjectMappingRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new CreateDrawingObjectMappingCommand(drawingId,
            request.PageNumber, request.ConstructionObjectId, request.ShapeType, request.Geometry), ct);
        return Created($"/api/drawings/{drawingId}/mappings?pageNumber={request.PageNumber}", result);
    }

    [HttpPut("api/drawing-object-mappings/{id:guid}")]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    public Task<DrawingObjectMappingDto> Update(Guid id, UpdateDrawingObjectMappingRequest request,
        CancellationToken ct) => sender.Send(new UpdateDrawingObjectMappingCommand(id,
            request.ConstructionObjectId, request.Geometry, request.RowVersion), ct);

    [HttpPost("api/drawing-object-mappings/{id:guid}/deactivate")]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    public Task<DeactivatedDrawingObjectMappingDto> Deactivate(Guid id,
        DeactivateDrawingObjectMappingRequest request, CancellationToken ct) =>
        sender.Send(new DeactivateDrawingObjectMappingCommand(id, request.RowVersion), ct);

    [HttpGet("api/construction-objects/{constructionObjectId:guid}/drawing-mappings")]
    public Task<ConstructionObjectDrawingMappingsDto> GetSources(Guid constructionObjectId,
        [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        sender.Send(new GetConstructionObjectDrawingMappingsQuery(constructionObjectId, pageNumber, pageSize), ct);

    [HttpPost("api/drawings/{drawingId:guid}/mappings/with-construction-object")]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    public async Task<ActionResult<ConstructionObjectWithDrawingMappingDto>> CreateWithObject(Guid drawingId,
        CreateConstructionObjectWithDrawingMappingRequest request, CancellationToken ct)
    {
        var result = await sender.Send(new CreateConstructionObjectWithDrawingMappingCommand(drawingId,
            request.PageNumber, request.ShapeType, request.Geometry, request.ParentId, request.Code,
            request.Name, request.ObjectType, request.Location, request.Description), ct);
        return Created($"/api/drawings/{drawingId}/mappings?pageNumber={request.PageNumber}", result);
    }
}

public sealed record CreateDrawingObjectMappingRequest(int PageNumber, Guid ConstructionObjectId,
    DrawingMappingShapeType ShapeType, DrawingRectangleDto Geometry);
public sealed record UpdateDrawingObjectMappingRequest(Guid ConstructionObjectId,
    DrawingRectangleDto Geometry, string RowVersion);
public sealed record DeactivateDrawingObjectMappingRequest(string RowVersion);
public sealed record CreateConstructionObjectWithDrawingMappingRequest(int PageNumber,
    DrawingMappingShapeType ShapeType, DrawingRectangleDto Geometry, Guid? ParentId,
    string Code, string Name, ConstructionObjectType ObjectType, string? Location, string? Description);
