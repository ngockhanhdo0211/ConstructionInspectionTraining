using FluentValidation;

namespace ConstructionInspection.Application.Inspections.Queries;

public sealed class GetInspectionsQueryValidator : AbstractValidator<GetInspectionsQuery>
{
    private static readonly string[] SortFields = ["number", "title", "status", "createdAt", "updatedAt"];
    public GetInspectionsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(250);
        RuleFor(x => x.SortBy).Must(x => SortFields.Contains(x, StringComparer.OrdinalIgnoreCase));
        RuleFor(x => x.SortDirection).Must(x => x.Equals("asc", StringComparison.OrdinalIgnoreCase)
            || x.Equals("desc", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class GetInspectionDetailQueryValidator : AbstractValidator<GetInspectionDetailQuery>
{
    public GetInspectionDetailQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetFormTemplateOptionsQueryValidator : AbstractValidator<GetFormTemplateOptionsQuery>
{
    public GetFormTemplateOptionsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(250);
    }
}
