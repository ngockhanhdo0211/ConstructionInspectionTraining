using ConstructionInspection.Application.Common.Interfaces;

namespace ConstructionInspection.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
