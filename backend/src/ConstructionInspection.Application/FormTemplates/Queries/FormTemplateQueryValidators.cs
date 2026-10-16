using FluentValidation;

namespace ConstructionInspection.Application.FormTemplates.Queries;

public sealed class GetFormTemplatesQueryValidator : AbstractValidator<GetFormTemplatesQuery>
{
    private static readonly string[] SortFields = ["code", "name", "createdAt", "updatedAt"];
    public GetFormTemplatesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1); RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200); RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.SortBy).Must(x => SortFields.Contains(x, StringComparer.OrdinalIgnoreCase)).WithMessage("sortBy is not supported.");
        RuleFor(x => x.SortDirection).Must(Direction).WithMessage("sortDirection must be 'asc' or 'desc'.");
    }
    private static bool Direction(string value) => value.Equals("asc", StringComparison.OrdinalIgnoreCase) || value.Equals("desc", StringComparison.OrdinalIgnoreCase);
}

public sealed class GetFormTemplateVersionsQueryValidator : AbstractValidator<GetFormTemplateVersionsQuery>
{
    private static readonly string[] SortFields = ["versionNumber", "createdAt", "publishedAt"];
    public GetFormTemplateVersionsQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1); RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SortBy).Must(x => SortFields.Contains(x, StringComparer.OrdinalIgnoreCase)).WithMessage("sortBy is not supported.");
        RuleFor(x => x.SortDirection).Must(x => x.Equals("asc", StringComparison.OrdinalIgnoreCase) || x.Equals("desc", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class GetFormTemplateVersionDetailQueryValidator : AbstractValidator<GetFormTemplateVersionDetailQuery>
{
    public GetFormTemplateVersionDetailQueryValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.VersionNumber).GreaterThan(0); }
}
