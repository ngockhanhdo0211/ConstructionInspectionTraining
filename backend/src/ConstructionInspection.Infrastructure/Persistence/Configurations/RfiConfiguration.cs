using ConstructionInspection.Domain.ConstructionObjects;
using ConstructionInspection.Domain.Rfis;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConstructionInspection.Infrastructure.Persistence.Configurations;

public sealed class RfiConfiguration : IEntityTypeConfiguration<Rfi>
{
    public void Configure(EntityTypeBuilder<Rfi> builder)
    {
        builder.ToTable("Rfis", table => table.HasCheckConstraint("CK_Rfis_NextEntryNumber", "[NextEntryNumber] > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Number).HasMaxLength(RfiRules.MaximumNumberLength).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(RfiRules.MaximumSubjectLength).IsRequired();
        builder.Property(x => x.Question).IsRequired();
        builder.Property(x => x.NextEntryNumber).IsRequired();
        builder.Property(x => x.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(x => x.UpdatedAt).HasPrecision(7);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.Number).IsUnique().HasDatabaseName("UX_Rfis_Number");
        builder.HasIndex(x => new { x.ConstructionObjectId, x.CreatedAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.CreatedByUserId, x.CreatedAt }).IsDescending(false, true);
        builder.HasOne<ConstructionObject>().WithMany().HasForeignKey(x => x.ConstructionObjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.RfiId).OnDelete(DeleteBehavior.NoAction);
        builder.Navigation(x => x.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RfiEntryConfiguration : IEntityTypeConfiguration<RfiEntry>
{
    public void Configure(EntityTypeBuilder<RfiEntry> builder)
    {
        builder.ToTable("RfiEntries", table =>
        {
            table.HasCheckConstraint("CK_RfiEntries_EntryNumber", "[EntryNumber] > 0");
            table.HasCheckConstraint("CK_RfiEntries_EntryType", "[EntryType] IN ('Response','ClarificationRequest')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntryType).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasPrecision(7).IsRequired();
        builder.HasIndex(x => new { x.RfiId, x.EntryNumber }).IsUnique().HasDatabaseName("UX_RfiEntries_RfiId_EntryNumber");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
