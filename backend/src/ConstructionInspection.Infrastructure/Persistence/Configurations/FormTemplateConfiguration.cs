using ConstructionInspection.Domain.FormTemplates;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConstructionInspection.Infrastructure.Persistence.Configurations;

public sealed class FormTemplateConfiguration : IEntityTypeConfiguration<FormTemplate>
{
    public void Configure(EntityTypeBuilder<FormTemplate> builder)
    {
        builder.ToTable("FormTemplates", table =>
        {
            table.HasCheckConstraint("CK_FormTemplates_Status", "[Status] IN ('Active','Inactive')");
            table.HasCheckConstraint("CK_FormTemplates_NextVersionNumber", "[NextVersionNumber] > 1");
            table.HasCheckConstraint(
                "CK_FormTemplates_UpdatedAudit",
                "([UpdatedByUserId] IS NULL AND [UpdatedAt] IS NULL) OR ([UpdatedByUserId] IS NOT NULL AND [UpdatedAt] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_FormTemplates_DeactivatedAudit",
                "([Status] = 'Active' AND [DeactivatedByUserId] IS NULL AND [DeactivatedAt] IS NULL) OR ([Status] = 'Inactive' AND [DeactivatedByUserId] IS NOT NULL AND [DeactivatedAt] IS NOT NULL)");
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Code).HasMaxLength(FormTemplateRules.MaximumCodeLength).IsRequired();
        builder.Property(item => item.Name).HasMaxLength(FormTemplateRules.MaximumNameLength).IsRequired();
        builder.Property(item => item.Description).HasMaxLength(FormTemplateRules.MaximumDescriptionLength);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(item => item.NextVersionNumber).HasDefaultValue(2).IsRequired();
        builder.Property(item => item.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(item => item.UpdatedAt).HasPrecision(7);
        builder.Property(item => item.DeactivatedAt).HasPrecision(7);
        builder.Property(item => item.RowVersion).IsRowVersion();

        builder.HasIndex(item => item.Code).IsUnique().HasDatabaseName("UX_FormTemplates_Code");
        builder.HasIndex(item => new { item.Status, item.Name }).HasDatabaseName("IX_FormTemplates_Status_Name");
        builder.HasIndex(item => item.CreatedByUserId).HasDatabaseName("IX_FormTemplates_CreatedByUserId");
        builder.HasIndex(item => item.UpdatedByUserId).HasDatabaseName("IX_FormTemplates_UpdatedByUserId");
        builder.HasIndex(item => item.DeactivatedByUserId).HasDatabaseName("IX_FormTemplates_DeactivatedByUserId");

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(item => item.DeactivatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
