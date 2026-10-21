using ConstructionInspection.Application.Common.Exceptions;
using ConstructionInspection.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ConstructionInspection.Domain.Drawings;
using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Domain.FormTemplates;
using ConstructionInspection.Domain.Inspections;
using ConstructionInspection.Domain.Checklists;

namespace ConstructionInspection.Infrastructure.Persistence;

public sealed class EfUnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            if (exception.Entries.Any(entry => entry.Entity is Checklist or ChecklistItem))
            {
                throw new ConflictException("checklist.concurrency_conflict", "The Checklist was changed by another request.");
            }
            if (exception.Entries.Any(entry => entry.Entity is Inspection or InspectionSubmission or BusinessProcess))
            {
                throw new ConflictException(
                    "inspection.concurrency_conflict",
                    "The Inspection was changed by another request.");
            }

            if (exception.Entries.Any(entry => entry.Entity is FormTemplate or FormTemplateVersion))
            {
                throw new ConflictException(
                    "form_template.concurrency_conflict",
                    "The Form Template was changed by another request.");
            }

            if (exception.Entries.Any(entry => entry.Entity is DrawingObjectMapping))
            {
                throw new ConflictException(
                    "drawing_mapping.concurrency_conflict",
                    "The Drawing mapping was changed by another request.");
            }

            if (exception.Entries.Any(entry => entry.Entity is DrawingDocument))
            {
                throw new ConflictException(
                    "drawing.concurrency_conflict",
                    "The Drawing metadata was changed by another request.");
            }

            throw new ConflictException(
                "construction_object.concurrency_conflict",
                "The Construction Object was changed by another request.");
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            var sqlMessage = exception.InnerException.Message;
            if (sqlMessage.Contains("UX_Checklists_Number", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("checklist.number_conflict", "The generated Checklist Number conflicts with an existing record.");
            if (sqlMessage.Contains("UX_BusinessProcesses_ChecklistId", StringComparison.OrdinalIgnoreCase)
                || sqlMessage.Contains("UX_ChecklistSubmissions_ChecklistId_SubmissionNumber", StringComparison.OrdinalIgnoreCase)
                || sqlMessage.Contains("UX_ChecklistItems_ChecklistId_Sequence", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("checklist.concurrency_conflict", "The Checklist was changed by another request.");
            if (sqlMessage.Contains("UX_Inspections_Number", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    "inspection.number_conflict",
                    "The generated Inspection Number conflicts with an existing record.");
            }

            if (sqlMessage.Contains("UX_BusinessProcesses_InspectionId", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    "inspection.process_conflict",
                    "The Inspection already has a Business Process.");
            }

            if (sqlMessage.Contains("UX_InspectionSubmissions_InspectionId_RevisionNumber", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    "inspection.concurrency_conflict",
                    "The Inspection revision number was allocated by another request.");
            }

            if (sqlMessage.Contains("UX_FormTemplates_Code", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    "form_template.code_duplicate",
                    "A Form Template with this Code already exists.");
            }

            if (sqlMessage.Contains("UX_FormTemplateVersions_OneDraft", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    "form_template.draft_exists",
                    "The Form Template already has a Draft version.");
            }

            if (sqlMessage.Contains("UX_FormTemplateVersions_TemplateId_VersionNumber", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    "form_template.concurrency_conflict",
                    "The Form Template version number was allocated by another request.");
            }

            if (exception.InnerException.Message.Contains(
                    "UX_DrawingObjectMappings_ActiveExact",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    "drawing_mapping.duplicate",
                    "An exact active Drawing mapping already exists.");
            }

            if (exception.Entries.Any(entry => entry.Entity is DrawingDocument))
            {
                throw new ConflictException(
                    "drawing.storage_key_conflict",
                    "The generated Drawing storage key conflicts with an existing record.");
            }

            throw new ConflictException(
                "construction_object.code_duplicate",
                "A Construction Object with this Code already exists.");
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
