using ConstructionInspection.Domain.Checklists;
using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConstructionInspection.Infrastructure.Persistence.Configurations;

public sealed class ChecklistConfiguration : IEntityTypeConfiguration<Checklist>
{
    public void Configure(EntityTypeBuilder<Checklist> builder)
    {
        builder.ToTable("Checklists", table => table.HasCheckConstraint("CK_Checklists_NextSubmissionNumber", "[NextSubmissionNumber] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(x => x.UpdatedAt).HasPrecision(7);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Number).IsUnique().HasDatabaseName("UX_Checklists_Number");
        builder.HasIndex(x => new { x.ConstructionObjectId, x.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.CreatedByUserId, x.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.ReviewerUserId, x.CreatedAt }).IsDescending(false, true);
        builder.HasOne<ConstructionObject>().WithMany().HasForeignKey(x => x.ConstructionObjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ChecklistSubmission>().WithMany().HasForeignKey(x => x.CurrentSubmissionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.ChecklistId).OnDelete(DeleteBehavior.NoAction);
        builder.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("ChecklistItems", table =>
        {
            table.HasCheckConstraint("CK_ChecklistItems_Sequence", "[Sequence] > 0");
            table.HasCheckConstraint("CK_ChecklistItems_Result", "[Result] IN ('NotChecked','Pass','Fail','NotApplicable')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Criterion).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Result).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(x => x.UpdatedAt).HasPrecision(7);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.ChecklistId, x.Sequence }).IsUnique().HasDatabaseName("UX_ChecklistItems_ChecklistId_Sequence");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ChecklistInspectionConfiguration : IEntityTypeConfiguration<ChecklistInspection>
{
    public void Configure(EntityTypeBuilder<ChecklistInspection> builder)
    {
        builder.ToTable("ChecklistInspections");
        builder.HasKey(x => new { x.ChecklistId, x.InspectionId });
        builder.Property(x => x.LinkedAt).HasPrecision(7).IsRequired();
        builder.HasOne<Checklist>().WithMany().HasForeignKey(x => x.ChecklistId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Inspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LinkedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ChecklistSubmissionConfiguration : IEntityTypeConfiguration<ChecklistSubmission>
{
    public void Configure(EntityTypeBuilder<ChecklistSubmission> builder)
    {
        builder.ToTable("ChecklistSubmissions", table => table.HasCheckConstraint("CK_ChecklistSubmissions_Number", "[SubmissionNumber] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubmittedAt).HasPrecision(7).IsRequired();
        builder.HasIndex(x => new { x.ChecklistId, x.SubmissionNumber }).IsUnique().IsDescending(false, true)
            .HasDatabaseName("UX_ChecklistSubmissions_ChecklistId_SubmissionNumber");
        builder.HasOne<Checklist>().WithMany().HasForeignKey(x => x.ChecklistId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ChecklistSubmissionItemConfiguration : IEntityTypeConfiguration<ChecklistSubmissionItem>
{
    public void Configure(EntityTypeBuilder<ChecklistSubmissionItem> builder)
    {
        builder.ToTable("ChecklistSubmissionItems", table =>
        {
            table.HasCheckConstraint("CK_ChecklistSubmissionItems_Sequence", "[Sequence] > 0");
            table.HasCheckConstraint("CK_ChecklistSubmissionItems_Result", "[Result] IN ('Pass','Fail','NotApplicable')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Criterion).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Result).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(2000);
        builder.HasIndex(x => new { x.ChecklistSubmissionId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.ChecklistSubmissionId, x.SourceChecklistItemId }).IsUnique();
        builder.HasOne<ChecklistSubmission>().WithMany().HasForeignKey(x => x.ChecklistSubmissionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ChecklistItem>().WithMany().HasForeignKey(x => x.SourceChecklistItemId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ChecklistSubmissionInspectionConfiguration : IEntityTypeConfiguration<ChecklistSubmissionInspection>
{
    public void Configure(EntityTypeBuilder<ChecklistSubmissionInspection> builder)
    {
        builder.ToTable("ChecklistSubmissionInspections");
        builder.HasKey(x => new { x.ChecklistSubmissionId, x.InspectionId });
        builder.HasOne<ChecklistSubmission>().WithMany().HasForeignKey(x => x.ChecklistSubmissionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Inspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(DeleteBehavior.NoAction);
    }
}
