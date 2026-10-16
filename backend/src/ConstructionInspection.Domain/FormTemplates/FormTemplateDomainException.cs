namespace ConstructionInspection.Domain.FormTemplates;

public sealed class FormTemplateDomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
