namespace ConstructionInspection.Domain.ConstructionObjects;

public sealed class ConstructionObject
{
    private ConstructionObject()
    {
    }

    public Guid Id { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public ConstructionObjectType ObjectType { get; private set; }
    public string? Location { get; private set; }
    public string? Description { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static ConstructionObject Create(
        Guid id,
        Guid? parentId,
        string code,
        string name,
        ConstructionObjectType objectType,
        string? location,
        string? description,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        ValidateIdentity(id, createdByUserId);
        ValidateValues(code, name, location, description);
        EnsureNotSelfParent(id, parentId);

        return new ConstructionObject
        {
            Id = id,
            ParentId = parentId,
            Code = ConstructionObjectRules.NormalizeCode(code),
            Name = name.Trim(),
            ObjectType = objectType,
            Location = NormalizeOptional(location),
            Description = NormalizeOptional(description),
            IsActive = true,
            CreatedByUserId = createdByUserId,
            CreatedAt = EnsureUtc(createdAtUtc)
        };
    }

    public void Update(
        Guid? parentId,
        string code,
        string name,
        ConstructionObjectType objectType,
        string? location,
        string? description,
        Guid updatedByUserId,
        DateTime updatedAtUtc)
    {
        if (!IsActive)
        {
            throw new ConstructionObjectDomainException(
                "construction_object.inactive",
                "An inactive Construction Object cannot be updated.");
        }

        ValidateIdentity(Id, updatedByUserId);
        ValidateValues(code, name, location, description);
        EnsureNotSelfParent(Id, parentId);
        if (ObjectType != objectType)
        {
            throw new ConstructionObjectDomainException(
                "construction_object.object_type_immutable",
                "ObjectType cannot be changed after creation.");
        }

        ParentId = parentId;
        Code = ConstructionObjectRules.NormalizeCode(code);
        Name = name.Trim();
        Location = NormalizeOptional(location);
        Description = NormalizeOptional(description);
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = EnsureUtc(updatedAtUtc);
    }

    public void Deactivate(Guid updatedByUserId, DateTime updatedAtUtc)
    {
        if (!IsActive)
        {
            throw new ConstructionObjectDomainException(
                "construction_object.already_inactive",
                "Construction Object is already inactive.");
        }

        if (updatedByUserId == Guid.Empty)
        {
            throw new ConstructionObjectDomainException(
                "construction_object.invalid_actor",
                "An update actor is required.");
        }

        IsActive = false;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = EnsureUtc(updatedAtUtc);
    }

    private static void ValidateIdentity(Guid id, Guid actorId)
    {
        if (id == Guid.Empty || actorId == Guid.Empty)
        {
            throw new ConstructionObjectDomainException(
                "construction_object.invalid_identity",
                "Construction Object and actor identifiers are required.");
        }
    }

    private static void EnsureNotSelfParent(Guid id, Guid? parentId)
    {
        if (parentId == id)
        {
            throw new ConstructionObjectDomainException(
                "construction_object.self_parent",
                "A Construction Object cannot be its own parent.");
        }
    }

    private static void ValidateValues(
        string code,
        string name,
        string? location,
        string? description)
    {
        var normalizedCode = ConstructionObjectRules.NormalizeCode(code);
        if (!ConstructionObjectRules.IsValidCode(normalizedCode))
        {
            throw new ConstructionObjectDomainException(
                "construction_object.invalid_code",
                "Code format is invalid.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > ConstructionObjectRules.MaximumNameLength)
        {
            throw new ConstructionObjectDomainException(
                "construction_object.invalid_name",
                "Name is required and must be within the allowed length.");
        }

        if (location?.Trim().Length > ConstructionObjectRules.MaximumLocationLength
            || description?.Trim().Length > ConstructionObjectRules.MaximumDescriptionLength)
        {
            throw new ConstructionObjectDomainException(
                "construction_object.invalid_text_length",
                "Location or description exceeds the allowed length.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
