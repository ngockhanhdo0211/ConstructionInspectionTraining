using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.ConstructionObjects.Interfaces;
using ConstructionInspection.Infrastructure.Authentication;
using ConstructionInspection.Infrastructure.Identity;
using ConstructionInspection.Infrastructure.Persistence;
using ConstructionInspection.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConstructionInspection.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' is not configured.");
            }

            options.UseSqlServer(connectionString);
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "JWT issuer is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "JWT audience is required.")
            .Validate(options => options.SigningKey.Length >= 32, "JWT signing key must be at least 32 characters.")
            .Validate(options => options.AccessTokenMinutes is > 0 and <= 1440, "JWT lifetime must be between 1 and 1440 minutes.")
            .ValidateOnStart();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<DevelopmentIdentitySeeder>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IConstructionObjectRepository, ConstructionObjectRepository>();
        services.AddScoped<IConstructionObjectReadService, ConstructionObjectReadService>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        return services;
    }
}
