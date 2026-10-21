namespace ConstructionInspection.Application.Checklists.Interfaces;

public interface IChecklistNumberSequence
{
    Task<long> NextAsync(CancellationToken cancellationToken);
}
