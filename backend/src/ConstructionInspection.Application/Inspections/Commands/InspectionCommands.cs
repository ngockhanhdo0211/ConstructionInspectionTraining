using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using ConstructionInspection.Application.Inspections.Interfaces;
using ConstructionInspection.Application.Inspections.Models;
using ConstructionInspection.Domain.FormTemplates;
using ConstructionInspection.Domain.Inspections;
using MediatR;

namespace ConstructionInspection.Application.Inspections.Commands;

public sealed record CreateInspectionCommand(
    Guid ConstructionObjectId, Guid FormTemplateId, string Title, string? Description, string DataJson)
    : IRequest<InspectionDetailDto>;

public sealed record SaveInspectionDraftCommand(
    Guid Id, string DataJson, string InspectionRowVersion,
    string SubmissionRowVersion, string ProcessRowVersion)
    : IRequest<InspectionDraftDto>;

public sealed class CreateInspectionCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IInspectionRepository repository,
    IInspectionReadService readService,
    IInspectionNumberSequence sequence,
    IFormIoDataValidator dataValidator,
    IUnitOfWork unitOfWork)
    : IRequestHandler<CreateInspectionCommand, InspectionDetailDto>
{
    public async Task<InspectionDetailDto> Handle(CreateInspectionCommand request, CancellationToken cancellationToken)
    {
        var actorId = InspectionAuthorization.RequireInspector(currentUser);
        var inspectionId = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var target = await repository.GetObjectForCreateAsync(request.ConstructionObjectId, ct)
                ?? throw new ResourceNotFoundException("construction_object.not_found", "The Construction Object was not found.");
            if (!target.IsActive)
                throw new ConflictException("inspection.construction_object_inactive", "The Construction Object is inactive.");

            var template = await repository.GetTemplateForCreateAsync(request.FormTemplateId, ct)
                ?? throw new ResourceNotFoundException("form_template.not_found", "The Form Template was not found.");
            if (!template.IsActive || !template.VersionId.HasValue || string.IsNullOrWhiteSpace(template.SchemaJson))
                throw new ConflictException("inspection.form_template_unavailable", "The Form Template has no available Published version.");

            var validatedData = dataValidator.ValidateDraft(template.SchemaJson, request.DataJson);
            var now = clock.UtcNow;
            var number = InspectionRules.FormatNumber(now, await sequence.GetNextAsync(ct));
            var id = Guid.NewGuid();
            var submissionId = Guid.NewGuid();
            var processId = Guid.NewGuid();
            var inspection = Inspection.Create(
                id, number, target.Id, template.VersionId.Value,
                request.Title, request.Description, actorId, now);
            repository.AddInspection(inspection);
            await unitOfWork.SaveChangesAsync(ct);

            var submission = InspectionSubmission.CreateInitial(
                submissionId, id, validatedData.CanonicalJson, actorId, now);
            inspection.SetCurrentSubmission(submissionId);
            var process = BusinessProcess.CreateDraft(processId, id, actorId, now);
            var history = ProcessHistory.CreateInitial(Guid.NewGuid(), processId, submissionId, actorId, now);
            repository.AddFoundation(submission, process, history);
            await unitOfWork.SaveChangesAsync(ct);
            return id;
        }, cancellationToken);

        return await readService.GetDetailAsync(
            inspectionId,
            new InspectionReadScope(actorId, false, true, false),
            cancellationToken)
            ?? throw new ResourceNotFoundException("inspection.not_found", "The created Inspection could not be read.");
    }
}

public sealed class SaveInspectionDraftCommandHandler(
    ICurrentUser currentUser,
    IClock clock,
    IInspectionRepository repository,
    IFormIoDataValidator dataValidator,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SaveInspectionDraftCommand, InspectionDraftDto>
{
    public Task<InspectionDraftDto> Handle(SaveInspectionDraftCommand request, CancellationToken cancellationToken)
    {
        var actorId = InspectionAuthorization.RequireInspector(currentUser);
        return unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var aggregate = await repository.GetForUpdateAsync(request.Id, ct)
                ?? throw new ResourceNotFoundException("inspection.not_found", "The Inspection was not found.");
            if (aggregate.Inspection.CreatedByUserId != actorId)
                throw new ResourceNotFoundException("inspection.not_found", "The Inspection was not found.");
            aggregate.Process.EnsureEditable();
            InspectionRowVersion.EnsureMatches(aggregate.Inspection.RowVersion, request.InspectionRowVersion, nameof(request.InspectionRowVersion));
            InspectionRowVersion.EnsureMatches(aggregate.Submission.RowVersion, request.SubmissionRowVersion, nameof(request.SubmissionRowVersion));
            InspectionRowVersion.EnsureMatches(aggregate.Process.RowVersion, request.ProcessRowVersion, nameof(request.ProcessRowVersion));

            var version = await repository.GetPinnedVersionAsync(aggregate.Inspection.FormTemplateVersionId, ct)
                ?? throw new ConflictException("inspection.form_template_unavailable", "The pinned Form Template version is unavailable.");
            var validated = dataValidator.ValidateDraft(version.SchemaJson, request.DataJson);
            var now = clock.UtcNow;
            var currentSubmission = aggregate.Submission;
            if (aggregate.Process.Status == ProcessStatus.Recheck && currentSubmission.SubmittedAt.HasValue)
            {
                var newSubmissionId = Guid.NewGuid();
                var revisionNumber = aggregate.Inspection.AllocateRevision(newSubmissionId, actorId, now);
                currentSubmission = InspectionSubmission.CreateRevision(
                    newSubmissionId,
                    aggregate.Inspection.Id,
                    revisionNumber,
                    aggregate.Submission.DataJson,
                    actorId,
                    now);
                currentSubmission.UpdateDraft(validated.CanonicalJson, actorId, now);
                repository.AddSubmission(currentSubmission);
                repository.AddHistory(ProcessHistory.Create(
                    Guid.NewGuid(), aggregate.Process.Id,
                    ProcessStatus.Recheck, ProcessStatus.Recheck,
                    "CreateRevision", actorId, now, currentSubmission.Id));
            }
            else
            {
                currentSubmission.UpdateDraft(validated.CanonicalJson, actorId, now);
                aggregate.Inspection.MarkDraftUpdated(actorId, now);
            }
            await unitOfWork.SaveChangesAsync(ct);
            return new InspectionDraftDto(
                aggregate.Inspection.Id,
                currentSubmission.RevisionNumber,
                currentSubmission.DataJson,
                now,
                Convert.ToBase64String(aggregate.Inspection.RowVersion),
                Convert.ToBase64String(currentSubmission.RowVersion),
                Convert.ToBase64String(aggregate.Process.RowVersion));
        }, cancellationToken);
    }
}
