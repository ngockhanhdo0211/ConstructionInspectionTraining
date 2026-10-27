using ConstructionInspection.Application.Common.Interfaces;

namespace ConstructionInspection.IntegrationTests.Support;

public sealed class TestClock : IClock
{
    private DateTime? fixedUtcNow;

    public DateTime UtcNow => fixedUtcNow ?? DateTime.UtcNow;

    public void Set(DateTime utcNow) =>
        fixedUtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public void UseSystemClock() => fixedUtcNow = null;
}
