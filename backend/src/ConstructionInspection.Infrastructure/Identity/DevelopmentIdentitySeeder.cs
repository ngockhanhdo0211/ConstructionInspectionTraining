using ConstructionInspection.Application.Common.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace ConstructionInspection.Infrastructure.Identity;

public sealed class DevelopmentIdentitySeeder(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IConfiguration configuration)
{
    private static readonly SeedUser[] Users =
    [
        new("inspector@local.test", "Demo Inspector", ApplicationRoles.Inspector),
        new("reviewer@local.test", "Demo Reviewer", ApplicationRoles.Reviewer),
        new("manager@local.test", "Demo Manager", ApplicationRoles.Manager),
        new("administrator@local.test", "Demo Administrator", ApplicationRoles.Administrator)
    ];

    public async Task SeedAsync()
    {
        var password = configuration["SeedUsers:DefaultPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Development seed password is not configured.");
        }

        foreach (var seedUser in Users)
        {
            await EnsureRoleAsync(seedUser.Role);

            var user = await userManager.FindByEmailAsync(seedUser.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = seedUser.Email,
                    Email = seedUser.Email,
                    EmailConfirmed = true,
                    FullName = seedUser.FullName,
                    IsActive = true,
                    LockoutEnabled = true,
                    CreatedAt = DateTime.UtcNow
                };

                EnsureSucceeded(
                    await userManager.CreateAsync(user, password),
                    $"create development user '{seedUser.Email}'");
            }

            if (!await userManager.IsInRoleAsync(user, seedUser.Role))
            {
                EnsureSucceeded(
                    await userManager.AddToRoleAsync(user, seedUser.Role),
                    $"assign role for development user '{seedUser.Email}'");
            }
        }
    }

    private async Task EnsureRoleAsync(string roleName)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            EnsureSucceeded(
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName)),
                $"create role '{roleName}'");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var codes = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"Failed to {operation}. Error codes: {codes}.");
    }

    private sealed record SeedUser(string Email, string FullName, string Role);
}
