namespace DesktopPet.Core;

public readonly record struct PetRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>Outcome of one movement tick.</summary>
/// <param name="X">The new horizontal position, always inside the working area.</param>
/// <param name="Arrived">True when the walk is finished and the pet should return to idle.</param>
public readonly record struct WalkStep(double X, bool Arrived);

public static class PetGeometry
{
    public static double ClampX(double x, PetRect area, double petWidth) => Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - petWidth));
    public static double BottomAlignedTop(PetRect area, double petHeight) => area.Bottom - petHeight;

    /// <summary>
    /// Moves at most <paramref name="maxStep"/> towards <paramref name="targetX"/> on one tick,
    /// stopping once within <paramref name="arriveEpsilon"/>. The result is always clamped into
    /// the working area, so a target beyond the edge can never walk the pet off-screen.
    /// </summary>
    public static WalkStep Step(double currentX, double targetX, double maxStep, PetRect area, double petWidth, double arriveEpsilon = 1.2, bool deadlineReached = false)
    {
        var distance = targetX - currentX;
        var x = currentX + Math.Sign(distance) * Math.Min(Math.Abs(distance), maxStep);
        x = ClampX(x, area, petWidth);
        return new WalkStep(x, Math.Abs(x - targetX) < arriveEpsilon || deadlineReached);
    }

    /// <summary>
    /// Picks a random stroll destination within <paramref name="range"/> of the current position,
    /// clamped so the whole pet stays inside the working area.
    /// </summary>
    public static double PickStrollTarget(double currentX, int range, PetRect area, double petWidth, int offset)
    {
        var bounded = Math.Clamp(offset, -range, range);
        return ClampX(currentX + bounded, area, petWidth);
    }
}
