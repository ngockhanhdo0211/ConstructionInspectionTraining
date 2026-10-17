using ConstructionInspection.Domain.Inspections;

namespace ConstructionInspection.Application.Inspections.Interfaces;

public sealed record InspectionObjectForCreate(Guid Id, bool IsActive);
public sealed record InspectionTemplateForCreate(
    Guid TemplateId, bool IsActive, Guid? VersionId, int? VersionNumber, string? SchemaJson);
public sealed record PinnedTemplateVersion(Guid Id, string SchemaJson);
public sealed record InspectionWriteAggregate(
    Inspection Inspection, InspectionSubmission Submission, BusinessProcess Process);

public interface IInspectionRepository
{
    Task<InspectionObjectForCreate?> GetObjectForCreateAsync(Guid id, CancellationToken cancellationToken);
    Task<InspectionTemplateForCreate?> GetTemplateForCreateAsync(Guid id, CancellationToken cancellationToken);
    Task<InspectionWriteAggregate?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken);
    Task<PinnedTemplateVersion?> GetPinnedVersionAsync(Guid id, CancellationToken cancellationToken);
    void AddInspection(Inspection inspection);
    void AddFoundation(InspectionSubmission submission, BusinessProcess process, ProcessHistory history);
}
