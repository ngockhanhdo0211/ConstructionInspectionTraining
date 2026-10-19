using ConstructionInspection.Domain.Inspections;
using FluentValidation;

namespace ConstructionInspection.Application.Inspections.Commands;

public sealed class SubmitInspectionCommandValidator : AbstractValidator<SubmitInspectionCommand>
{
    public SubmitInspectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ReviewerUserId).NotEmpty();
        RuleFor(x => x.InspectionRowVersion).NotEmpty();
        RuleFor(x => x.SubmissionRowVersion).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class StartInspectionReviewCommandValidator : AbstractValidator<StartInspectionReviewCommand>
{
    public StartInspectionReviewCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class ApproveInspectionCommandValidator : AbstractValidator<ApproveInspectionCommand>
{
    public ApproveInspectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(InspectionRules.MaximumWorkflowNoteLength);
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class RejectInspectionCommandValidator : AbstractValidator<RejectInspectionCommand>
{
    public RejectInspectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(InspectionRules.MaximumWorkflowNoteLength);
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class RequestInspectionRecheckCommandValidator : AbstractValidator<RequestInspectionRecheckCommand>
{
    public RequestInspectionRecheckCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(InspectionRules.MaximumWorkflowNoteLength);
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}

public sealed class ReassignInspectionReviewerCommandValidator : AbstractValidator<ReassignInspectionReviewerCommand>
{
    public ReassignInspectionReviewerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ReviewerUserId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(InspectionRules.MaximumWorkflowNoteLength);
        RuleFor(x => x.InspectionRowVersion).NotEmpty();
        RuleFor(x => x.ProcessRowVersion).NotEmpty();
    }
}
