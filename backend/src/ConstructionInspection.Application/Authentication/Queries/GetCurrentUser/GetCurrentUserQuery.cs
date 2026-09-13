using MediatR;

namespace ConstructionInspection.Application.Authentication.Queries.GetCurrentUser;

public sealed record GetCurrentUserQuery : IRequest<CurrentUserResponse>;
