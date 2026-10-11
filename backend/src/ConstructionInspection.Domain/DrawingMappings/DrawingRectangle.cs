namespace ConstructionInspection.Domain.DrawingMappings;

public readonly record struct DrawingRectangle(decimal Left, decimal Bottom, decimal Right, decimal Top)
{
    public const int DecimalPlaces = 6;

    public static DrawingRectangle Create(decimal left, decimal bottom, decimal right, decimal top)
    {
        var value = new DrawingRectangle(
            Normalize(left),
            Normalize(bottom),
            Normalize(right),
            Normalize(top));

        if (value.Left < 0m || value.Right > 1m || value.Left >= value.Right
            || value.Bottom < 0m || value.Top > 1m || value.Bottom >= value.Top)
        {
            throw new DrawingObjectMappingDomainException(
                "drawing_mapping.invalid_geometry",
                "Rectangle coordinates must satisfy 0 <= Left < Right <= 1 and 0 <= Bottom < Top <= 1.");
        }

        return value;
    }

    private static decimal Normalize(decimal value) =>
        decimal.Round(value, DecimalPlaces, MidpointRounding.AwayFromZero);
}
