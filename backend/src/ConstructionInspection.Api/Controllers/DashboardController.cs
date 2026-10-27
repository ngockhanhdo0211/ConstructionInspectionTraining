using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Application.Dashboard.Models;
using ConstructionInspection.Application.Dashboard.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Authorize(Roles = ApplicationRoles.Administrator + "," + ApplicationRoles.Manager + ","
    + ApplicationRoles.Inspector + "," + ApplicationRoles.Reviewer)]
[Route("api/dashboard")]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardResponseDto>(StatusCodes.Status200OK)]
    public Task<DashboardResponseDto> Get(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "private, no-store";
        return sender.Send(new GetDashboardQuery(), cancellationToken);
    }
}
