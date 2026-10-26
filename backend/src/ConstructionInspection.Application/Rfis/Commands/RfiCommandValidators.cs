using ConstructionInspection.Domain.Rfis;
using FluentValidation;

namespace ConstructionInspection.Application.Rfis.Commands;

public sealed class CreateRfiCommandValidator : AbstractValidator<CreateRfiCommand>
{ public CreateRfiCommandValidator() { RuleFor(x => x.ConstructionObjectId).NotEmpty(); RuleFor(x => x.Subject).NotEmpty().MaximumLength(RfiRules.MaximumSubjectLength); RuleFor(x => x.Question).NotEmpty(); } }
public sealed class SaveRfiDraftCommandValidator : AbstractValidator<SaveRfiDraftCommand>
{ public SaveRfiDraftCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Subject).NotEmpty().MaximumLength(RfiRules.MaximumSubjectLength); RuleFor(x => x.Question).NotEmpty(); RuleFor(x => x.RfiRowVersion).NotEmpty(); RuleFor(x => x.ProcessRowVersion).NotEmpty(); } }
public sealed class SubmitRfiCommandValidator : AbstractValidator<SubmitRfiCommand>
{ public SubmitRfiCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.ResponderUserId).NotEmpty(); RuleFor(x => x.RfiRowVersion).NotEmpty(); RuleFor(x => x.ProcessRowVersion).NotEmpty(); } }
public sealed class ReassignRfiCommandValidator : AbstractValidator<ReassignRfiCommand>
{ public ReassignRfiCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.ResponderUserId).NotEmpty(); RuleFor(x => x.Reason).NotEmpty().MaximumLength(RfiRules.MaximumReasonLength); RuleFor(x => x.RfiRowVersion).NotEmpty(); RuleFor(x => x.ProcessRowVersion).NotEmpty(); } }
public sealed class RespondRfiCommandValidator : AbstractValidator<RespondRfiCommand>
{ public RespondRfiCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.ResponseText).NotEmpty(); RuleFor(x => x.RfiRowVersion).NotEmpty(); RuleFor(x => x.ProcessRowVersion).NotEmpty(); } }
public sealed class RequestRfiClarificationCommandValidator : AbstractValidator<RequestRfiClarificationCommand>
{ public RequestRfiClarificationCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Content).NotEmpty(); RuleFor(x => x.RfiRowVersion).NotEmpty(); RuleFor(x => x.ProcessRowVersion).NotEmpty(); } }
public sealed class CloseRfiCommandValidator : AbstractValidator<CloseRfiCommand>
{ public CloseRfiCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Comment).MaximumLength(RfiRules.MaximumReasonLength); RuleFor(x => x.RfiRowVersion).NotEmpty(); RuleFor(x => x.ProcessRowVersion).NotEmpty(); } }
public sealed class CancelRfiCommandValidator : AbstractValidator<CancelRfiCommand>
{ public CancelRfiCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Reason).NotEmpty().MaximumLength(RfiRules.MaximumReasonLength); RuleFor(x => x.RfiRowVersion).NotEmpty(); RuleFor(x => x.ProcessRowVersion).NotEmpty(); } }
