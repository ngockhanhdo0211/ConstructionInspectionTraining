using ConstructionInspection.Domain.DrawingMappings;

namespace ConstructionInspection.Domain.UnitTests.DrawingMappings;

public sealed class DrawingObjectMappingTests
{
    [Fact]
    public void Rectangle_NormalizesToSixDecimals()
    {
        var value = DrawingRectangle.Create(0.12345649m, 0.2m, 0.98765451m, 0.9m);

        Assert.Equal(0.123456m, value.Left);
        Assert.Equal(0.987655m, value.Right);
    }

    [Theory]
    [InlineData(-0.1, 0.1, 0.2, 0.2)]
    [InlineData(0.1, 0.1, 1.1, 0.2)]
    [InlineData(0.2, 0.1, 0.2, 0.2)]
    [InlineData(0.3, 0.1, 0.2, 0.2)]
    [InlineData(0.1, 0.2, 0.2, 0.2)]
    [InlineData(0.1, 0.3, 0.2, 0.2)]
    public void Rectangle_RejectsInvalidCoordinates(double left, double bottom, double right, double top)
    {
        var error = Assert.Throws<DrawingObjectMappingDomainException>(() => DrawingRectangle.Create(
            (decimal)left, (decimal)bottom, (decimal)right, (decimal)top));
        Assert.Equal("drawing_mapping.invalid_geometry", error.Code);
    }

    [Fact]
    public void Rectangle_RejectsZeroWidthAfterRounding()
    {
        Assert.Throws<DrawingObjectMappingDomainException>(() =>
            DrawingRectangle.Create(0.1000001m, 0.2m, 0.1000004m, 0.3m));
    }

    [Fact]
    public void Mapping_AllowsOverlapAndMaintainsLifecycleAudit()
    {
        var actor = Guid.NewGuid();
        var first = Create(actor, DrawingRectangle.Create(0.1m, 0.1m, 0.5m, 0.5m));
        var overlapping = Create(actor, DrawingRectangle.Create(0.4m, 0.4m, 0.8m, 0.8m));

        Assert.True(first.IsActive);
        Assert.True(overlapping.IsActive);
        var updatedAt = new DateTime(2026, 10, 11, 1, 0, 0, DateTimeKind.Utc);
        first.Update(Guid.NewGuid(), DrawingRectangle.Create(0.2m, 0.2m, 0.6m, 0.6m), actor, updatedAt);
        Assert.Equal(updatedAt, first.UpdatedAt);
        first.Deactivate(actor, updatedAt.AddMinutes(1));
        Assert.False(first.IsActive);
        Assert.Equal(actor, first.DeactivatedByUserId);
        Assert.Throws<DrawingObjectMappingDomainException>(() => first.Update(
            Guid.NewGuid(), DrawingRectangle.Create(0.1m, 0.1m, 0.2m, 0.2m), actor, updatedAt));
    }

    private static DrawingObjectMapping Create(Guid actor, DrawingRectangle rectangle) =>
        DrawingObjectMapping.Create(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(),
            DrawingMappingShapeType.Rectangle, rectangle, actor, DateTime.UtcNow);
}
