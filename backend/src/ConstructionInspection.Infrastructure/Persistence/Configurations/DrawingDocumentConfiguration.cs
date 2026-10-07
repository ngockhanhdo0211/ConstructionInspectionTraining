using ConstructionInspection.Domain.Drawings;
using ConstructionInspection.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConstructionInspection.Infrastructure.Persistence.Configurations;

public sealed class DrawingDocumentConfiguration : IEntityTypeConfiguration<DrawingDocument>
{
    public void Configure(EntityTypeBuilder<DrawingDocument> builder)
    {
        builder.ToTable("DrawingDocuments", t => t.HasCheckConstraint("CK_DrawingDocuments_Status",
            "[Status] IN ('Uploading','Ready','Failed')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(500).IsUnicode(false).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.CreatedAt).HasPrecision(7).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.StorageKey).IsUnique().HasDatabaseName("UX_DrawingDocuments_StorageKey");
        builder.HasIndex(x => x.Sha256).HasDatabaseName("IX_DrawingDocuments_Sha256");
        builder.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_DrawingDocuments_Status_CreatedAt");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
