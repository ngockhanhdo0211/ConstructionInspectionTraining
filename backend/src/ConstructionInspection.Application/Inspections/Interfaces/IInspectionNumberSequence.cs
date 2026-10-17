namespace ConstructionInspection.Application.Inspections.Interfaces;

public interface IInspectionNumberSequence
{
    Task<long> GetNextAsync(CancellationToken cancellationToken);
}
