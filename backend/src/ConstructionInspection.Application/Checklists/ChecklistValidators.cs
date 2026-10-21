using ConstructionInspection.Application.Checklists.Commands;
using ConstructionInspection.Application.Checklists.Queries;
using ConstructionInspection.Domain.Checklists;
using FluentValidation;

namespace ConstructionInspection.Application.Checklists;

public sealed class CreateChecklistCommandValidator : AbstractValidator<CreateChecklistCommand>
{
    public CreateChecklistCommandValidator()
    {
        RuleFor(x => x.ConstructionObjectId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(ChecklistRules.MaximumTitleLength);
        RuleFor(x => x.Description).MaximumLength(ChecklistRules.MaximumDescriptionLength);
        RuleFor(x => x.Items).NotNull().Must(x => x.Count is >= 1 and <= ChecklistRules.MaximumItems);
        RuleForEach(x => x.Items).ChildRules(item =>
            item.RuleFor(x => x.Criterion).NotEmpty().MaximumLength(ChecklistRules.MaximumCriterionLength));
        RuleFor(x => x.Items).Must(items => items.Select(x => x.Criterion.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == items.Count)
            .WithMessage("Checklist item criteria must be unique.");
    }
}

public sealed class UpdateChecklistItemsCommandValidator : AbstractValidator<UpdateChecklistItemsCommand>
{
    public UpdateChecklistItemsCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ChecklistRowVersion).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
        RuleFor(x => x.Items).NotNull().Must(x => x.Count is >= 1 and <= ChecklistRules.MaximumItems);
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(x => x.Criterion).NotEmpty().MaximumLength(ChecklistRules.MaximumCriterionLength);
            item.RuleFor(x => x.Note).MaximumLength(ChecklistRules.MaximumNoteLength);
            item.RuleFor(x => x.RowVersion).NotEmpty().When(x => x.Id.HasValue);
        });
    }
}

public sealed class GetChecklistsQueryValidator : AbstractValidator<GetChecklistsQuery>
{
    private static readonly string[] Sorts = ["number", "title", "status", "createdAt", "updatedAt"];
    public GetChecklistsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.SortBy).Must(x => Sorts.Contains(x, StringComparer.OrdinalIgnoreCase));
        RuleFor(x => x.SortDirection).Must(x => x is "asc" or "desc");
    }
}

public sealed class StartChecklistCommandValidator : AbstractValidator<StartChecklistCommand>
{
    public StartChecklistCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ChecklistRowVersion).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class ReplaceChecklistInspectionsCommandValidator : AbstractValidator<ReplaceChecklistInspectionsCommand>
{
    public ReplaceChecklistInspectionsCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.InspectionIds).NotNull();
        RuleForEach(x => x.InspectionIds).NotEmpty();
        RuleFor(x => x.ChecklistRowVersion).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class SubmitChecklistCommandValidator : AbstractValidator<SubmitChecklistCommand>
{
    public SubmitChecklistCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ReviewerUserId).NotEmpty();
        RuleFor(x => x.ChecklistRowVersion).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class ApproveChecklistCommandValidator : AbstractValidator<ApproveChecklistCommand>
{
    public ApproveChecklistCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(ChecklistRules.MaximumNoteLength);
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class RejectChecklistCommandValidator : AbstractValidator<RejectChecklistCommand>
{
    public RejectChecklistCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(ChecklistRules.MaximumNoteLength);
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class RequestChecklistRecheckCommandValidator : AbstractValidator<RequestChecklistRecheckCommand>
{
    public RequestChecklistRecheckCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(ChecklistRules.MaximumNoteLength);
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class StartChecklistReviewCommandValidator : AbstractValidator<StartChecklistReviewCommand>
{
    public StartChecklistReviewCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class GetChecklistHistoryQueryValidator : AbstractValidator<GetChecklistHistoryQuery>
{
    public GetChecklistHistoryQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetChecklistSubmissionsQueryValidator : AbstractValidator<GetChecklistSubmissionsQuery>
{
    public GetChecklistSubmissionsQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetChecklistSubmissionQueryValidator : AbstractValidator<GetChecklistSubmissionQuery>
{
    public GetChecklistSubmissionQueryValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.SubmissionNumber).GreaterThan(0);
    }
}
