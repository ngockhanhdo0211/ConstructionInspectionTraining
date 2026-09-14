using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConstructionInspection.Infrastructure.Persistence.Configurations;

public sealed class ConstructionObjectConfiguration : IEntityTypeConfiguration<ConstructionObject>
{
    public void Configure(EntityTypeBuilder<ConstructionObject> builder)
    {
        builder.ToTable("ConstructionObjects", table =>
            table.HasCheckConstraint(
                "CK_ConstructionObjects_ObjectType",
                "[ObjectType] IN ('Project','Building','Floor','Room','WorkItem')"));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(250).IsRequired();
        builder.Property(item => item.ObjectType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(item => item.Location).HasMaxLength(500);
        builder.Property(item => item.Description).HasMaxLength(2000);
        builder.Property(item => item.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(item => item.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(item => item.UpdatedAt).HasPrecision(7);
        builder.Property(item => item.RowVersion).IsRowVersion();

        builder.HasIndex(item => item.Code)
            .IsUnique()
            .HasDatabaseName("UX_ConstructionObjects_Code");
        builder.HasIndex(item => item.ParentId)
            .HasDatabaseName("IX_ConstructionObjects_ParentId");
        builder.HasIndex(item => new { item.IsActive, item.ObjectType })
            .HasDatabaseName("IX_ConstructionObjects_IsActive_ObjectType");

        builder.HasOne<ConstructionObject>()
            .WithMany()
            .HasForeignKey(item => item.ParentId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.CreatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(item => item.UpdatedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
