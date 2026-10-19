using FluentValidation;

namespace ConstructionInspection.Application.Inspections.Queries;

public sealed class GetReviewerOptionsQueryValidator : AbstractValidator<GetReviewerOptionsQuery>
{
    public GetReviewerOptionsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(250);
    }
}

public sealed class GetInspectionHistoryQueryValidator : AbstractValidator<GetInspectionHistoryQuery>
{
    public GetInspectionHistoryQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetInspectionRevisionsQueryValidator : AbstractValidator<GetInspectionRevisionsQuery>
{
    public GetInspectionRevisionsQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetInspectionRevisionDetailQueryValidator : AbstractValidator<GetInspectionRevisionDetailQuery>
{
    public GetInspectionRevisionDetailQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.RevisionNumber).GreaterThan(0);
    }
}
