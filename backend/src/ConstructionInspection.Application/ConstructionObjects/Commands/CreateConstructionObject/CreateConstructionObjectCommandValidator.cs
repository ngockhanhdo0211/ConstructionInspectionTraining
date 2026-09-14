using FluentValidation;

namespace ConstructionInspection.Application.ConstructionObjects.Commands.CreateConstructionObject;

public sealed class CreateConstructionObjectCommandValidator
    : AbstractValidator<CreateConstructionObjectCommand>
{
    public CreateConstructionObjectCommandValidator()
    {
        ConstructionObjectCommandValidation.AddCommonRules(
            this,
            command => command.Code,
            command => command.Name,
            command => command.ObjectType,
            command => command.Location,
            command => command.Description);
    }
}
