using FluentValidation;

namespace ConstructionInspection.Application.Drawings.Queries.GetDrawings;

public sealed class GetDrawingsQueryValidator : AbstractValidator<GetDrawingsQuery>
{
    private static readonly string[] Sorts = ["filename", "filesize", "pagecount", "createdat", "status"];
    public GetDrawingsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SortBy).Must(x => Sorts.Contains(x, StringComparer.OrdinalIgnoreCase));
        RuleFor(x => x.SortDirection).Must(x => x is "asc" or "desc");
    }
}
