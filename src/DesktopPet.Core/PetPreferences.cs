namespace DesktopPet.Core;

public sealed record PetPreferences(string MonitorId, double X, bool IsPaused, bool IsManualSleeping)
{
    public static PetPreferences Default { get; } = new("PRIMARY", 0, false, false);
}
