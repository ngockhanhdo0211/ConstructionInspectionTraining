namespace ConstructionInspection.Application.FormTemplates.Interfaces;

public sealed record ValidatedFormIoSchema(string CanonicalJson, int InputUtf8ByteCount);

public interface IFormIoSchemaValidator
{
    ValidatedFormIoSchema Validate(string schemaJson);
}
