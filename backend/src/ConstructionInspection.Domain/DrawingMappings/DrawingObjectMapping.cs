namespace ConstructionInspection.Domain.DrawingMappings;

public sealed class DrawingObjectMapping
{
    private DrawingObjectMapping() { }

    public Guid Id { get; private set; }
    public Guid DrawingDocumentId { get; private set; }
    public int PageNumber { get; private set; }
    public Guid ConstructionObjectId { get; private set; }
    public DrawingMappingShapeType ShapeType { get; private set; }
    public decimal Left { get; private set; }
    public decimal Bottom { get; private set; }
    public decimal Right { get; private set; }
    public decimal Top { get; private set; }
    public DrawingObjectMappingStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTime? DeactivatedAt { get; private set; }
    public Guid? DeactivatedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive => Status == DrawingObjectMappingStatus.Active;

    public static DrawingObjectMapping Create(
        Guid id, Guid drawingDocumentId, int pageNumber, Guid constructionObjectId,
        DrawingMappingShapeType shapeType, DrawingRectangle rectangle,
        Guid createdByUserId, DateTime createdAtUtc)
    {
        ValidateIdentity(id, drawingDocumentId, constructionObjectId, createdByUserId);
        if (pageNumber <= 0)
            throw Invalid("drawing_mapping.invalid_page", "PageNumber must be greater than zero.");
        if (shapeType != DrawingMappingShapeType.Rectangle)
            throw Invalid("drawing_mapping.invalid_shape_type", "Only Rectangle mappings are supported.");

        var canonical = DrawingRectangle.Create(rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Top);
        return new DrawingObjectMapping
        {
            Id = id,
            DrawingDocumentId = drawingDocumentId,
            PageNumber = pageNumber,
            ConstructionObjectId = constructionObjectId,
            ShapeType = shapeType,
            Left = canonical.Left,
            Bottom = canonical.Bottom,
            Right = canonical.Right,
            Top = canonical.Top,
            Status = DrawingObjectMappingStatus.Active,
            CreatedByUserId = createdByUserId,
            CreatedAt = EnsureUtc(createdAtUtc)
        };
    }

    public void Update(Guid constructionObjectId, DrawingRectangle rectangle, Guid updatedByUserId, DateTime updatedAtUtc)
    {
        EnsureActive("updated");
        if (constructionObjectId == Guid.Empty || updatedByUserId == Guid.Empty)
            throw InvalidIdentity();

        var canonical = DrawingRectangle.Create(rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Top);
        ConstructionObjectId = constructionObjectId;
        Left = canonical.Left;
        Bottom = canonical.Bottom;
        Right = canonical.Right;
        Top = canonical.Top;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = EnsureUtc(updatedAtUtc);
    }

    public void Deactivate(Guid deactivatedByUserId, DateTime deactivatedAtUtc)
    {
        EnsureActive("deactivated");
        if (deactivatedByUserId == Guid.Empty)
            throw InvalidIdentity();

        Status = DrawingObjectMappingStatus.Inactive;
        DeactivatedByUserId = deactivatedByUserId;
        DeactivatedAt = EnsureUtc(deactivatedAtUtc);
        UpdatedByUserId = deactivatedByUserId;
        UpdatedAt = DeactivatedAt;
    }

    private void EnsureActive(string action)
    {
        if (!IsActive)
            throw Invalid("drawing_mapping.inactive", $"An inactive Drawing mapping cannot be {action}.");
    }

    private static void ValidateIdentity(Guid id, Guid drawingId, Guid objectId, Guid actorId)
    {
        if (id == Guid.Empty || drawingId == Guid.Empty || objectId == Guid.Empty || actorId == Guid.Empty)
            throw InvalidIdentity();
    }

    private static DrawingObjectMappingDomainException InvalidIdentity() => Invalid(
        "drawing_mapping.invalid_identity",
        "Mapping, Drawing, Construction Object and actor identifiers are required.");

    private static DrawingObjectMappingDomainException Invalid(string code, string message) => new(code, message);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
