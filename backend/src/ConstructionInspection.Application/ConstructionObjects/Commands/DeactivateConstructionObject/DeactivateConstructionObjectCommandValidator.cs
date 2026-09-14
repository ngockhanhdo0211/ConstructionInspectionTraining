using FluentValidation;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.DeactivateConstructionObject;

public sealed class DeactivateConstructionObjectCommandValidator
    : AbstractValidator<DeactivateConstructionObjectCommand>
{
    public DeactivateConstructionObjectCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.RowVersion)
            .Must(ConstructionObjectCommandValidation.IsValidRowVersion)
            .WithMessage("rowVersion must be valid Base64.");
    }
}
