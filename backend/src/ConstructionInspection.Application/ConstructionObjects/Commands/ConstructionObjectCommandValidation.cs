using FluentValidation;
using ConstructionInspection.Domain.ConstructionObjects;
using System.Linq.Expressions;

namespace ConstructionInspection.Application.ConstructionObjects.Commands;

internal static class ConstructionObjectCommandValidation
{
    public static void AddCommonRules<T>(
        AbstractValidator<T> validator,
        Expression<Func<T, string>> code,
        Expression<Func<T, string>> name,
        Expression<Func<T, ConstructionObjectType>> objectType,
        Expression<Func<T, string?>> location,
        Expression<Func<T, string?>> description)
    {
        validator.RuleFor(code)
            .NotEmpty()
            .Must(value => ConstructionObjectRules.IsValidCode(
                ConstructionObjectRules.NormalizeCode(value)))
            .WithMessage("Code must be 2-100 characters and contain only Latin letters, numbers, '.', '_' or '-'.");
        validator.RuleFor(name)
            .NotEmpty()
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .MaximumLength(ConstructionObjectRules.MaximumNameLength);
        validator.RuleFor(objectType).IsInEnum();
        validator.RuleFor(location).MaximumLength(ConstructionObjectRules.MaximumLocationLength);
        validator.RuleFor(description).MaximumLength(ConstructionObjectRules.MaximumDescriptionLength);
    }

    public static bool IsValidRowVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(value).Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
