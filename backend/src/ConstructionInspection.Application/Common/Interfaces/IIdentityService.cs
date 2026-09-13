using ConstructionInspection.Application.Common.Models;

namespace ConstructionInspection.Application.Common.Interfaces;

public interface IIdentityService
{
    Task<UserProfile?> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<UserProfile?> GetUserAsync(Guid userId, CancellationToken cancellationToken);
}
