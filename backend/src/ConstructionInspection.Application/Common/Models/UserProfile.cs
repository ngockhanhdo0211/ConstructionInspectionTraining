namespace ConstructionInspection.Application.Common.Models;

public sealed record UserProfile(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles);
