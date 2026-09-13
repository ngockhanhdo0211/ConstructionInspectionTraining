using MediatR;

namespace ConstructionInspection.Application.Authentication.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResponse>;
