using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Common.Models;
using ConstructionInspection.Application.Drawings.Commands.UploadDrawing;
using ConstructionInspection.Application.Drawings.Models;
using ConstructionInspection.Application.Drawings.Queries.GetDrawingDetail;
using ConstructionInspection.Application.Drawings.Queries.GetDrawings;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Administrator + "," + ApplicationRoles.Manager)]
[Route("api/drawings")]
public sealed class DrawingsController(ISender sender) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = ApplicationRoles.Administrator)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(52L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 52L * 1024 * 1024)]
    public async Task<ActionResult<UploadDrawingResponse>> Upload([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null)
        {
            ModelState.AddModelError("file", "A PDF file is required.");
            return ValidationProblem(ModelState);
        }
        await using var stream = file.OpenReadStream();
        var result = await sender.Send(new UploadDrawingCommand(file.FileName, stream), ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Drawing.Id }, result);
    }

    [HttpGet]
    public Task<PagedResult<DrawingListItemDto>> Get([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] string sortBy = "createdat",
        [FromQuery] string sortDirection = "desc", CancellationToken ct = default) =>
        sender.Send(new GetDrawingsQuery(pageNumber, pageSize, search, sortBy, sortDirection), ct);

    [HttpGet("{id:guid}")]
    public Task<DrawingDetailDto> GetById(Guid id, CancellationToken ct) => sender.Send(new GetDrawingDetailQuery(id), ct);
}
