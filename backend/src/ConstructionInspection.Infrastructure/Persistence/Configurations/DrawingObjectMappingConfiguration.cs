using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Domain.Drawings;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConstructionInspection.Infrastructure.Persistence.Configurations;

public sealed class DrawingObjectMappingConfiguration : IEntityTypeConfiguration<DrawingObjectMapping>
{
    public void Configure(EntityTypeBuilder<DrawingObjectMapping> builder)
    {
        builder.ToTable("DrawingObjectMappings", table =>
        {
            table.HasCheckConstraint("CK_DrawingObjectMappings_PageNumber", "[PageNumber] > 0");
            table.HasCheckConstraint("CK_DrawingObjectMappings_ShapeType", "[ShapeType] = 'Rectangle'");
            table.HasCheckConstraint("CK_DrawingObjectMappings_Status", "[Status] IN ('Active','Inactive')");
            table.HasCheckConstraint("CK_DrawingObjectMappings_Geometry",
                "[Left] >= 0 AND [Left] < [Right] AND [Right] <= 1 AND [Bottom] >= 0 AND [Bottom] < [Top] AND [Top] <= 1");
            table.HasCheckConstraint("CK_DrawingObjectMappings_DeactivationAudit",
                "([Status] = 'Active' AND [DeactivatedAt] IS NULL AND [DeactivatedByUserId] IS NULL) OR " +
                "([Status] = 'Inactive' AND [DeactivatedAt] IS NOT NULL AND [DeactivatedByUserId] IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.IsActive);
        builder.Property(x => x.ShapeType).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.Left).HasColumnType("decimal(9,6)").IsRequired();
        builder.Property(x => x.Bottom).HasColumnType("decimal(9,6)").IsRequired();
        builder.Property(x => x.Right).HasColumnType("decimal(9,6)").IsRequired();
        builder.Property(x => x.Top).HasColumnType("decimal(9,6)").IsRequired();
        builder.Property(x => x.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(x => x.UpdatedAt).HasPrecision(7);
        builder.Property(x => x.DeactivatedAt).HasPrecision(7);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.DrawingDocumentId, x.PageNumber, x.Status })
            .HasDatabaseName("IX_DrawingObjectMappings_DrawingDocumentId_PageNumber_Status");
        builder.HasIndex(x => new { x.ConstructionObjectId, x.Status })
            .HasDatabaseName("IX_DrawingObjectMappings_ConstructionObjectId_Status");
        builder.HasIndex(x => new
            {
                x.DrawingDocumentId, x.PageNumber, x.ConstructionObjectId,
                x.Left, x.Bottom, x.Right, x.Top
            })
            .IsUnique()
            .HasFilter("[Status] = 'Active'")
            .HasDatabaseName("UX_DrawingObjectMappings_ActiveExact");

        builder.HasOne<DrawingDocument>().WithMany().HasForeignKey(x => x.DrawingDocumentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ConstructionObject>().WithMany().HasForeignKey(x => x.ConstructionObjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DeactivatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
