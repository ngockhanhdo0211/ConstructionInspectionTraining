namespace ConstructionInspection.Domain.Drawings;

public static class DrawingDocumentRules
{
    public const long MaximumFileSize = 50L * 1024 * 1024;
    public const int MaximumPageCount = 2000;
    public const int MaximumFileNameLength = 255;
    public const int Sha256HexLength = 64;
    public const int MaximumStorageKeyLength = 500;
}
