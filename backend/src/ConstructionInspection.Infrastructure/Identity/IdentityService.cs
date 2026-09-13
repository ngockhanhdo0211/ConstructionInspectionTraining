using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Common.Models;
using Microsoft.AspNetCore.Identity;

namespace ConstructionInspection.Infrastructure.Identity;

public sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager)
    : IIdentityService
{
    public async Task<UserProfile?> AuthenticateAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        var result = await signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);

        return result.Succeeded
            ? await CreateProfileAsync(user)
            : null;
    }

    public async Task<UserProfile?> GetUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is { IsActive: true }
            ? await CreateProfileAsync(user)
            : null;
    }

    private async Task<UserProfile> CreateProfileAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return new UserProfile(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            roles.OrderBy(role => role).ToArray());
    }
}
