using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.FormTemplates;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConstructionInspection.Infrastructure.Persistence.Configurations;

public sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("Inspections", table =>
            table.HasCheckConstraint("CK_Inspections_NextRevisionNumber", "[NextRevisionNumber] >= 2"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(InspectionRules.MaximumNumberLength).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(InspectionRules.MaximumTitleLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(InspectionRules.MaximumDescriptionLength);
        builder.Property(x => x.NextRevisionNumber).IsRequired();
        builder.Property(x => x.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(x => x.UpdatedAt).HasPrecision(7);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Number).IsUnique().HasDatabaseName("UX_Inspections_Number");
        builder.HasIndex(x => new { x.ConstructionObjectId, x.CreatedAt }).IsDescending(false, true)
            .HasDatabaseName("IX_Inspections_ConstructionObjectId_CreatedAt");
        builder.HasIndex(x => new { x.CreatedByUserId, x.CreatedAt }).IsDescending(false, true)
            .HasDatabaseName("IX_Inspections_CreatedByUserId_CreatedAt");
        builder.HasIndex(x => new { x.ReviewerUserId, x.CreatedAt }).IsDescending(false, true)
            .HasDatabaseName("IX_Inspections_ReviewerUserId_CreatedAt");
        builder.HasIndex(x => x.FormTemplateVersionId).HasDatabaseName("IX_Inspections_FormTemplateVersionId");
        builder.HasOne<ConstructionObject>().WithMany().HasForeignKey(x => x.ConstructionObjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<FormTemplateVersion>().WithMany().HasForeignKey(x => x.FormTemplateVersionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InspectionSubmission>().WithMany().HasForeignKey(x => x.CurrentSubmissionId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class InspectionSubmissionConfiguration : IEntityTypeConfiguration<InspectionSubmission>
{
    public void Configure(EntityTypeBuilder<InspectionSubmission> builder)
    {
        builder.ToTable("InspectionSubmissions", table =>
        {
            table.HasCheckConstraint("CK_InspectionSubmissions_RevisionNumber", "[RevisionNumber] > 0");
            table.HasCheckConstraint("CK_InspectionSubmissions_DataJson", "ISJSON([DataJson]) = 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DataJson).IsRequired();
        builder.Property(x => x.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(x => x.UpdatedAt).HasPrecision(7);
        builder.Property(x => x.SubmittedAt).HasPrecision(7);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.InspectionId, x.RevisionNumber }).IsUnique().IsDescending(false, true)
            .HasDatabaseName("UX_InspectionSubmissions_InspectionId_RevisionNumber");
        builder.HasOne<Inspection>().WithMany().HasForeignKey(x => x.InspectionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class BusinessProcessConfiguration : IEntityTypeConfiguration<BusinessProcess>
{
    public void Configure(EntityTypeBuilder<BusinessProcess> builder)
    {
        builder.ToTable("BusinessProcesses", table =>
        {
            table.HasCheckConstraint("CK_BusinessProcesses_EntityType", "[EntityType] = 'Inspection'");
            table.HasCheckConstraint(
                "CK_BusinessProcesses_Status",
                "[Status] IN ('Draft','Submitted','UnderReview','Approved','Recheck','Rejected')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(x => x.UpdatedAt).HasPrecision(7);
        builder.Property(x => x.DueAt).HasPrecision(7);
        builder.Property(x => x.CompletedAt).HasPrecision(7);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.InspectionId).IsUnique().HasDatabaseName("UX_BusinessProcesses_InspectionId");
        builder.HasIndex(x => new { x.EntityType, x.Status, x.UpdatedAt }).IsDescending(false, false, true)
            .HasDatabaseName("IX_BusinessProcesses_EntityType_Status_UpdatedAt");
        builder.HasOne<Inspection>().WithOne().HasForeignKey<BusinessProcess>(x => x.InspectionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ProcessHistoryConfiguration : IEntityTypeConfiguration<ProcessHistory>
{
    public void Configure(EntityTypeBuilder<ProcessHistory> builder)
    {
        builder.ToTable("ProcessHistories", table =>
        {
            table.HasCheckConstraint(
                "CK_ProcessHistories_ToStatus",
                "[ToStatus] IN ('Draft','Submitted','UnderReview','Approved','Recheck','Rejected')");
            table.HasCheckConstraint(
                "CK_ProcessHistories_Action",
                "[Action] IN ('Create','Submit','StartReview','Approve','Reject','RequestRecheck','ReassignReviewer','CreateRevision')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(2000);
        builder.Property(x => x.PerformedAt).HasPrecision(7).IsRequired();
        builder.HasIndex(x => new { x.BusinessProcessId, x.PerformedAt, x.Id })
            .HasDatabaseName("IX_ProcessHistories_BusinessProcessId_PerformedAt_Id");
        builder.HasOne<BusinessProcess>().WithMany().HasForeignKey(x => x.BusinessProcessId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PreviousAssignedToUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.NewAssignedToUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<InspectionSubmission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.NoAction);
    }
}
