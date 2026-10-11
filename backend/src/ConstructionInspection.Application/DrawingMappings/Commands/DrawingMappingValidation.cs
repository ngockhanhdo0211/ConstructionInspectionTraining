using ConstructionInspection.Domain.DrawingMappings;
using ConstructionInspection.Application.DrawingMappings.Models;

namespace ConstructionInspection.Application.DrawingMappings.Commands;

internal static class DrawingMappingValidation
{
    public static bool IsValidGeometry(DrawingRectangleDto value)
    {
        try
        {
            _ = DrawingRectangle.Create(value.Left, value.Bottom, value.Right, value.Top);
            return true;
        }
        catch (DrawingObjectMappingDomainException)
        {
            return false;
        }
    }
}
