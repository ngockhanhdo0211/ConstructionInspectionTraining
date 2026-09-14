using FluentValidation;

namespace ConstructionInspection.Application.ConstructionObjects.Queries.GetConstructionObjectDetail;

public sealed class GetConstructionObjectDetailQueryValidator
    : AbstractValidator<GetConstructionObjectDetailQuery>
{
    public GetConstructionObjectDetailQueryValidator()
    {
        RuleFor(query => query.Id).NotEmpty();
    }
}
