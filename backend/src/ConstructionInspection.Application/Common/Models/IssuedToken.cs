namespace ConstructionInspection.Application.Common.Models;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
