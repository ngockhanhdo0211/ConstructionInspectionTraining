using ConstructionInspection.Domain.Inspections;
using FluentValidation;

namespace ConstructionInspection.Application.Inspections.Commands;

public sealed class CreateInspectionCommandValidator : AbstractValidator<CreateInspectionCommand>
{
    public CreateInspectionCommandValidator()
    {
        RuleFor(x => x.ConstructionObjectId).NotEmpty();
        RuleFor(x => x.FormTemplateId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(InspectionRules.MaximumTitleLength);
        RuleFor(x => x.Description).MaximumLength(InspectionRules.MaximumDescriptionLength);
        RuleFor(x => x.DataJson).NotEmpty();
    }
}

public sealed class SaveInspectionDraftCommandValidator : AbstractValidator<SaveInspectionDraftCommand>
{
    public SaveInspectionDraftCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DataJson).NotEmpty();
        RuleFor(x => x.InspectionRowVersion).NotEmpty();
        RuleFor(x => x.SubmissionRowVersion).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}
