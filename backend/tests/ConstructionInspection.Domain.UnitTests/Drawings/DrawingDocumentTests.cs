using ConstructionInspection.Domain.Drawings;

namespace ConstructionInspection.Domain.UnitTests.Drawings;

public sealed class DrawingDocumentTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateUploading_WithValidMetadata_SetsAuditAndUploadingStatus()
    {
        var drawing = Create();
        Assert.Equal(DrawingDocumentStatus.Uploading, drawing.Status);
        Assert.Equal(UserId, drawing.CreatedByUserId);
        Assert.Equal("plan.pdf", drawing.OriginalFileName);
    }

    [Fact]
    public void MarkReady_OnlyAllowsUploadingTransition()
    {
        var drawing = Create();
        drawing.MarkReady();
        Assert.Equal(DrawingDocumentStatus.Ready, drawing.Status);
        Assert.Throws<DrawingDocumentDomainException>(drawing.MarkReady);
    }

    [Fact]
    public void MarkFailed_OnlyAllowsUploadingTransition()
    {
        var drawing = Create();
        drawing.MarkFailed();
        Assert.Equal(DrawingDocumentStatus.Failed, drawing.Status);
        Assert.Throws<DrawingDocumentDomainException>(drawing.MarkReady);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2001)]
    public void CreateUploading_WithInvalidPageCount_Throws(int pageCount) =>
        Assert.Throws<DrawingDocumentDomainException>(() => Create(pageCount));

    private static DrawingDocument Create(int pageCount = 2) => DrawingDocument.CreateUploading(
        Guid.NewGuid(), @"C:\fake\plan.pdf", $"drawings/2026/10/{Guid.NewGuid():N}/x.pdf", 100,
        new string('a', 64), pageCount, UserId, Now);
}
