namespace DesktopPet.Core;

public static class StrollSchedule
{
    public const int MinimumIdleSeconds = 30;
    public const int MaximumIdleSeconds = 60;

    public static DateTime NextWalkTime(DateTime from, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return from.AddSeconds(random.Next(MinimumIdleSeconds, MaximumIdleSeconds + 1));
    }
}
