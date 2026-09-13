using ConstructionInspection.Application.Authentication.Commands.Login;
using ConstructionInspection.Application.Authentication.Queries.GetCurrentUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionInspection.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(
            new LoginCommand(request.Email, request.Password),
            cancellationToken);
        return Ok(response);
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<GetCurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<GetCurrentUserResponse>> Me(
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
        return Ok(new GetCurrentUserResponse(
            result.Id,
            result.Email,
            result.FullName,
            result.Roles));
    }
}

public sealed record LoginRequest(string Email, string Password);

public sealed record GetCurrentUserResponse(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles);
