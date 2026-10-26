using FluentValidation;

namespace ConstructionInspection.Application.Rfis.Queries;

public sealed class GetRfisQueryValidator : AbstractValidator<GetRfisQuery>
{
    public GetRfisQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.SortBy).Must(x => new[] { "createdAt", "updatedAt", "number", "subject", "status", "dueAtUtc" }.Contains(x, StringComparer.OrdinalIgnoreCase));
        RuleFor(x => x.SortDirection).Must(x => x.Equals("asc", StringComparison.OrdinalIgnoreCase) || x.Equals("desc", StringComparison.OrdinalIgnoreCase));
    }
}
public sealed class GetRfiDetailQueryValidator : AbstractValidator<GetRfiDetailQuery>
{ public GetRfiDetailQueryValidator() => RuleFor(x => x.Id).NotEmpty(); }
public sealed class GetRfiHistoryQueryValidator : AbstractValidator<GetRfiHistoryQuery>
{ public GetRfiHistoryQueryValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }
public sealed class GetRfiResponderOptionsQueryValidator : AbstractValidator<GetRfiResponderOptionsQuery>
{ public GetRfiResponderOptionsQueryValidator() { RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); RuleFor(x => x.Search).MaximumLength(200); } }
