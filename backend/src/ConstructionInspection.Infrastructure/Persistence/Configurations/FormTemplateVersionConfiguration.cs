using ConstructionInspection.Domain.FormTemplates;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConstructionInspection.Infrastructure.Persistence.Configurations;

public sealed class FormTemplateVersionConfiguration : IEntityTypeConfiguration<FormTemplateVersion>
{
    public void Configure(EntityTypeBuilder<FormTemplateVersion> builder)
    {
        builder.ToTable("FormTemplateVersions", table =>
        {
            table.HasCheckConstraint("CK_FormTemplateVersions_VersionNumber", "[VersionNumber] > 0");
            table.HasCheckConstraint("CK_FormTemplateVersions_Status", "[Status] IN ('Draft','Published')");
            table.HasCheckConstraint("CK_FormTemplateVersions_SchemaJson", "ISJSON([SchemaJson]) = 1");
            table.HasCheckConstraint(
                "CK_FormTemplateVersions_UpdatedAudit",
                "([UpdatedByUserId] IS NULL AND [UpdatedAt] IS NULL) OR ([UpdatedByUserId] IS NOT NULL AND [UpdatedAt] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_FormTemplateVersions_PublishedAudit",
                "([Status] = 'Draft' AND [PublishedByUserId] IS NULL AND [PublishedAt] IS NULL) OR ([Status] = 'Published' AND [PublishedByUserId] IS NOT NULL AND [PublishedAt] IS NOT NULL)");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(item => item.SchemaJson).IsRequired();
        builder.Property(item => item.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(item => item.UpdatedAt).HasPrecision(7);
        builder.Property(item => item.PublishedAt).HasPrecision(7);
        builder.Property(item => item.RowVersion).IsRowVersion();

        builder.HasIndex(item => new { item.FormTemplateId, item.VersionNumber })
            .IsUnique().IsDescending(false, true)
            .HasDatabaseName("UX_FormTemplateVersions_TemplateId_VersionNumber");
        builder.HasIndex(item => item.FormTemplateId)
            .IsUnique().HasFilter("[Status] = 'Draft'").HasDatabaseName("UX_FormTemplateVersions_OneDraft");
        builder.HasIndex(item => new { item.FormTemplateId, item.Status, item.VersionNumber })
            .IsDescending(false, false, true).HasDatabaseName("IX_FormTemplateVersions_TemplateId_Status_VersionNumber");
        builder.HasIndex(item => item.CreatedByUserId).HasDatabaseName("IX_FormTemplateVersions_CreatedByUserId");
        builder.HasIndex(item => item.UpdatedByUserId).HasDatabaseName("IX_FormTemplateVersions_UpdatedByUserId");
        builder.HasIndex(item => item.PublishedByUserId).HasDatabaseName("IX_FormTemplateVersions_PublishedByUserId");

        builder.HasOne<FormTemplate>().WithMany().HasForeignKey(item => item.FormTemplateId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.PublishedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
