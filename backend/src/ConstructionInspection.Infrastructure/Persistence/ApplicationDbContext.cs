using ConstructionInspection.Application.Common.Authorization;
using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.Drawings;
using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<ConstructionObject> ConstructionObjects => Set<ConstructionObject>();
    public DbSet<DrawingDocument> DrawingDocuments => Set<DrawingDocument>();
    public DbSet<DrawingObjectMapping> DrawingObjectMappings => Set<DrawingObjectMapping>();
    private static readonly Guid InspectorRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReviewerRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ManagerRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AdministratorRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(item => item.FullName)
                .HasMaxLength(200)
                .IsRequired();

            user.Property(item => item.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            user.Property(item => item.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .ValueGeneratedOnAdd()
                .IsRequired();

            user.Property(item => item.UpdatedAt)
                .IsRequired(false);

            user.HasIndex(item => item.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .HasFilter("[NormalizedEmail] IS NOT NULL")
                .IsUnique();
        });

        builder.Entity<IdentityRole<Guid>>().HasData(
            CreateRole(InspectorRoleId, ApplicationRoles.Inspector),
            CreateRole(ReviewerRoleId, ApplicationRoles.Reviewer),
            CreateRole(ManagerRoleId, ApplicationRoles.Manager),
            CreateRole(AdministratorRoleId, ApplicationRoles.Administrator));
    }

    private static IdentityRole<Guid> CreateRole(Guid id, string name)
    {
        return new IdentityRole<Guid>
        {
            Id = id,
            Name = name,
            NormalizedName = name,
            ConcurrencyStamp = id.ToString()
        };
    }
}
