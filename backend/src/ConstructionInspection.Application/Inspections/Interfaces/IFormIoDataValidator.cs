namespace ConstructionInspection.Application.Inspections.Interfaces;

public sealed record ValidatedFormIoData(string CanonicalJson, int Utf8ByteCount);

public interface IFormIoDataValidator
{
    ValidatedFormIoData ValidateDraft(string schemaJson, string dataJson);
}
