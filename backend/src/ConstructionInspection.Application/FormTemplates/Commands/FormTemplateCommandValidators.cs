using ConstructionInspection.Domain.FormTemplates;
using FluentValidation;

namespace ConstructionInspection.Application.FormTemplates.Commands;

public sealed class CreateFormTemplateCommandValidator : AbstractValidator<CreateFormTemplateCommand>
{
    public CreateFormTemplateCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(FormTemplateRules.MaximumCodeLength)
            .Matches("^[A-Za-z0-9][A-Za-z0-9._-]{1,49}$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FormTemplateRules.MaximumNameLength);
        RuleFor(x => x.Description).MaximumLength(FormTemplateRules.MaximumDescriptionLength);
        RuleFor(x => x.SchemaJson).NotEmpty();
    }
}

public sealed class UpdateFormTemplateDraftCommandValidator : AbstractValidator<UpdateFormTemplateDraftCommand>
{
    public UpdateFormTemplateDraftCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.VersionNumber).GreaterThan(0);
        RuleFor(x => x.SchemaJson).NotEmpty(); RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class PublishFormTemplateVersionCommandValidator : AbstractValidator<PublishFormTemplateVersionCommand>
{
    public PublishFormTemplateVersionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.VersionNumber).GreaterThan(0);
        RuleFor(x => x.TemplateRowVersion).NotEmpty(); RuleFor(x => x.VersionRowVersion).NotEmpty();
    }
}

public sealed class CreateFormTemplateVersionCommandValidator : AbstractValidator<CreateFormTemplateVersionCommand>
{
    public CreateFormTemplateVersionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.SchemaJson).NotEmpty();
        RuleFor(x => x.TemplateRowVersion).NotEmpty();
    }
}

public sealed class DeactivateFormTemplateCommandValidator : AbstractValidator<DeactivateFormTemplateCommand>
{
    public DeactivateFormTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.RowVersion).NotEmpty();
    }
}
