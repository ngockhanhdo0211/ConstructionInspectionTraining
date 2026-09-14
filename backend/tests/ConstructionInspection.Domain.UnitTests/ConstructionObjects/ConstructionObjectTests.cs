using ConstructionInspection.Domain.ConstructionObjects;

namespace ConstructionInspection.Domain.UnitTests.ConstructionObjects;

public sealed class ConstructionObjectTests
{
    private static readonly Guid ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime CreatedAt = new(2026, 9, 13, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_TrimsAndNormalizesCode()
    {
        var item = Create(code: "  room-a-03.301  ");

        Assert.Equal("ROOM-A-03.301", item.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("ROOM 301")]
    [InlineData("#ROOM")]
    public void Create_WithInvalidCode_Throws(string code)
    {
        Assert.Throws<ConstructionObjectDomainException>(() => Create(code: code));
    }

    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Assert.Throws<ConstructionObjectDomainException>(() => Create(name: "  "));
    }

    [Theory]
    [InlineData(ConstructionObjectType.Project, null, true)]
    [InlineData(ConstructionObjectType.Building, ConstructionObjectType.Project, true)]
    [InlineData(ConstructionObjectType.Floor, ConstructionObjectType.Building, true)]
    [InlineData(ConstructionObjectType.Room, ConstructionObjectType.Floor, true)]
    [InlineData(ConstructionObjectType.WorkItem, ConstructionObjectType.Project, false)]
    [InlineData(ConstructionObjectType.WorkItem, ConstructionObjectType.Building, false)]
    [InlineData(ConstructionObjectType.WorkItem, ConstructionObjectType.Floor, false)]
    [InlineData(ConstructionObjectType.WorkItem, ConstructionObjectType.Room, true)]
    [InlineData(ConstructionObjectType.Project, ConstructionObjectType.Project, false)]
    [InlineData(ConstructionObjectType.Room, ConstructionObjectType.Building, false)]
    [InlineData(ConstructionObjectType.Building, ConstructionObjectType.WorkItem, false)]
    public void ParentTypeRule_ReturnsExpected(
        ConstructionObjectType child,
        ConstructionObjectType? parent,
        bool expected)
    {
        Assert.Equal(expected, ConstructionObjectRules.IsParentTypeValid(child, parent));
    }

    [Fact]
    public void Update_ChangesValuesAndAudit()
    {
        var item = Create();
        var updatedAt = CreatedAt.AddHours(1);

        item.Update(
            Guid.NewGuid(),
            "room-302",
            "Room 302",
            ConstructionObjectType.Room,
            "Level 3",
            "Updated",
            ActorId,
            updatedAt);

        Assert.Equal("ROOM-302", item.Code);
        Assert.Equal(ActorId, item.UpdatedByUserId);
        Assert.Equal(updatedAt, item.UpdatedAt);
    }

    [Fact]
    public void Create_SetsCreatedByUserId()
    {
        var item = Create();

        Assert.Equal(ActorId, item.CreatedByUserId);
    }

    [Fact]
    public void Create_WithSelfParent_Throws()
    {
        var id = Guid.NewGuid();

        var exception = Assert.Throws<ConstructionObjectDomainException>(() => ConstructionObject.Create(
            id, id, "ROOM-301", "Room 301", ConstructionObjectType.Room,
            null, null, ActorId, CreatedAt));

        Assert.Equal("construction_object.self_parent", exception.Code);
    }

    [Fact]
    public void Update_WithDifferentObjectType_Throws()
    {
        var item = Create();

        var exception = Assert.Throws<ConstructionObjectDomainException>(() => item.Update(
            item.ParentId, item.Code, item.Name, ConstructionObjectType.WorkItem,
            item.Location, item.Description, ActorId, CreatedAt.AddMinutes(1)));

        Assert.Equal("construction_object.object_type_immutable", exception.Code);
        Assert.Equal(ConstructionObjectType.Room, item.ObjectType);
    }

    [Fact]
    public void Update_InactiveObject_Throws()
    {
        var item = Create();
        item.Deactivate(ActorId, CreatedAt.AddMinutes(1));

        Assert.Throws<ConstructionObjectDomainException>(() => item.Update(
            item.ParentId, item.Code, item.Name, item.ObjectType,
            item.Location, item.Description, ActorId, CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Deactivate_ChangesStateAndCannotRepeat()
    {
        var item = Create();
        item.Deactivate(ActorId, CreatedAt.AddMinutes(1));

        Assert.False(item.IsActive);
        Assert.Throws<ConstructionObjectDomainException>(() =>
            item.Deactivate(ActorId, CreatedAt.AddMinutes(2)));
    }

    private static ConstructionObject Create(
        string code = "ROOM-301",
        string name = "Room 301") => ConstructionObject.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            code,
            name,
            ConstructionObjectType.Room,
            null,
            null,
            ActorId,
            CreatedAt);
}
