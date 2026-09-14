using FluentValidation;

namespace ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjects;

public sealed class GetConstructionObjectsQueryValidator
    : AbstractValidator<GetConstructionObjectsQuery>
{
    private static readonly string[] AllowedSortFields =
        ["code", "name", "objectType", "createdAt", "updatedAt"];

    public GetConstructionObjectsQueryValidator()
    {
        RuleFor(query => query.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Search).MaximumLength(200);
        RuleFor(query => query.ObjectType).IsInEnum().When(query => query.ObjectType.HasValue);
        RuleFor(query => query.ParentId)
            .NotNull()
            .When(query => query.ParentFilterMode == Models.ParentFilterMode.Specific);
        RuleFor(query => query.SortBy)
            .Must(value => AllowedSortFields.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("sortBy is not supported.");
        RuleFor(query => query.SortDirection)
            .Must(value => value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortDirection must be 'asc' or 'desc'.");
    }
}
