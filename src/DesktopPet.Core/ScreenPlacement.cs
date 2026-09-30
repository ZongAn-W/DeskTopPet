namespace DesktopPet.Core;

public readonly record struct MonitorArea(string Id, double Left, double Top, double Width, double Height)
{
    public PetRect WorkingArea => new(Left, Top, Width, Height);
}

public readonly record struct PetPlacement(string MonitorId, double Left, double Top);

public static class ScreenPlacement
{
    public static PetPlacement Resolve(PetPreferences saved, IReadOnlyList<MonitorArea> monitors, string primaryId, double petWidth, double petHeight)
    {
        var monitor = monitors.FirstOrDefault(m => m.Id == saved.MonitorId);
        if (string.IsNullOrWhiteSpace(monitor.Id)) monitor = monitors.FirstOrDefault(m => m.Id == primaryId);
        if (string.IsNullOrWhiteSpace(monitor.Id)) monitor = monitors[0];
        var x = saved.MonitorId == monitor.Id && double.IsFinite(saved.X) ? saved.X : monitor.Left + monitor.Width - petWidth;
        return new PetPlacement(monitor.Id, PetGeometry.ClampX(x, monitor.WorkingArea, petWidth), PetGeometry.BottomAlignedTop(monitor.WorkingArea, petHeight));
    }
}
