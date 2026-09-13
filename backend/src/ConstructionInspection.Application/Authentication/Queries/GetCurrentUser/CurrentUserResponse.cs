namespace ConstructionInspection.Application.Authentication.Queries.GetCurrentUser;

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles);
