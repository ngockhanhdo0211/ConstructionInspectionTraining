namespace ConstructionInspection.Application.Authentication.Commands.Login;

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    LoginUserResponse User);

public sealed record LoginUserResponse(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles);
