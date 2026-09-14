using FluentValidation;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.UpdateConstructionObject;

public sealed class UpdateConstructionObjectCommandValidator
    : AbstractValidator<UpdateConstructionObjectCommand>
{
    public UpdateConstructionObjectCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.RowVersion)
            .Must(ConstructionObjectCommandValidation.IsValidRowVersion)
            .WithMessage("rowVersion must be valid Base64.");
        ConstructionObjectCommandValidation.AddCommonRules(
            this,
            command => command.Code,
            command => command.Name,
            command => command.ObjectType,
            command => command.Location,
            command => command.Description);
    }
}
