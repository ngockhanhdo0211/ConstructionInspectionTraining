using ConstructionInspection.Domain.FormTemplates;

namespace ConstructionInspection.Domain.UnitTests.FormTemplates;

public sealed class FormTemplateTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime CreatedAt = new(2026, 10, 14, 1, 2, 3, DateTimeKind.Utc);

    [Fact]
    public void Create_NormalizesCodeAndInitializesLifecycle()
    {
        var template = FormTemplate.Create(Guid.NewGuid(), "  safety-form  ", " Safety form ", null, ActorId, CreatedAt);

        Assert.Equal("SAFETY-FORM", template.Code);
        Assert.Equal("Safety form", template.Name);
        Assert.Equal(FormTemplateStatus.Active, template.Status);
        Assert.Equal(2, template.NextVersionNumber);
        Assert.Equal(ActorId, template.CreatedByUserId);
        Assert.Equal(CreatedAt, template.CreatedAt);
        Assert.Null(template.UpdatedAt);
        Assert.Null(template.DeactivatedAt);
    }

    [Fact]
    public void AllocateNextVersion_UsesCounterAndUpdatesAudit()
    {
        var template = CreateTemplate();
        var changedAt = CreatedAt.AddMinutes(1);

        var allocated = template.AllocateNextVersion(ActorId, changedAt);

        Assert.Equal(2, allocated);
        Assert.Equal(3, template.NextVersionNumber);
        Assert.Equal(ActorId, template.UpdatedByUserId);
        Assert.Equal(changedAt, template.UpdatedAt);
    }

    [Fact]
    public void Deactivate_IsOneWayAndWritesAudit()
    {
        var template = CreateTemplate();
        var changedAt = CreatedAt.AddMinutes(2);

        template.Deactivate(ActorId, changedAt);

        Assert.Equal(FormTemplateStatus.Inactive, template.Status);
        Assert.Equal(ActorId, template.DeactivatedByUserId);
        Assert.Equal(changedAt, template.DeactivatedAt);
        var error = Assert.Throws<FormTemplateDomainException>(() => template.Deactivate(ActorId, changedAt));
        Assert.Equal("form_template.already_inactive", error.Code);
    }

    [Fact]
    public void InactiveTemplate_CannotAllocateOrPublishVersionActions()
    {
        var template = CreateTemplate();
        template.Deactivate(ActorId, CreatedAt.AddMinutes(1));

        var allocationError = Assert.Throws<FormTemplateDomainException>(
            () => template.AllocateNextVersion(ActorId, CreatedAt.AddMinutes(2)));
        var publishError = Assert.Throws<FormTemplateDomainException>(
            () => template.MarkPublished(ActorId, CreatedAt.AddMinutes(2)));

        Assert.Equal("form_template.template_inactive", allocationError.Code);
        Assert.Equal("form_template.template_inactive", publishError.Code);
    }

    private static FormTemplate CreateTemplate() =>
        FormTemplate.Create(Guid.NewGuid(), "FORM-01", "Form", null, ActorId, CreatedAt);
}

public sealed class FormTemplateVersionTests
{
    private static readonly Guid ActorId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime CreatedAt = new(2026, 10, 14, 1, 2, 3, DateTimeKind.Utc);

    [Fact]
    public void CreateDraft_InitializesDraftAudit()
    {
        var version = CreateDraft();

        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(FormTemplateVersionStatus.Draft, version.Status);
        Assert.Equal(ActorId, version.CreatedByUserId);
        Assert.Null(version.PublishedAt);
    }

    [Fact]
    public void Draft_CanBeUpdatedAndPublished()
    {
        var version = CreateDraft();
        var updateTime = CreatedAt.AddMinutes(1);
        var publishTime = CreatedAt.AddMinutes(2);

        version.UpdateDraft("{\"components\":[]}", ActorId, updateTime);
        version.Publish(ActorId, publishTime);

        Assert.Equal(FormTemplateVersionStatus.Published, version.Status);
        Assert.Equal(updateTime, version.UpdatedAt);
        Assert.Equal(ActorId, version.PublishedByUserId);
        Assert.Equal(publishTime, version.PublishedAt);
    }

    [Fact]
    public void PublishedVersion_IsImmutableAndCannotBePublishedAgain()
    {
        var version = CreateDraft();
        version.Publish(ActorId, CreatedAt.AddMinutes(1));

        var updateError = Assert.Throws<FormTemplateDomainException>(
            () => version.UpdateDraft("{\"components\":[]}", ActorId, CreatedAt.AddMinutes(2)));
        var publishError = Assert.Throws<FormTemplateDomainException>(
            () => version.Publish(ActorId, CreatedAt.AddMinutes(2)));

        Assert.Equal("form_template.version_published", updateError.Code);
        Assert.Equal("form_template.invalid_transition", publishError.Code);
    }

    private static FormTemplateVersion CreateDraft() => FormTemplateVersion.CreateDraft(
        Guid.NewGuid(), Guid.NewGuid(), 1, "{\"components\":[]}", ActorId, CreatedAt);
}
