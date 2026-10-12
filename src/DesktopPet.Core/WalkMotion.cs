namespace DesktopPet.Core;

/// <summary>Accumulates time-based travel without losing distance when native windows snap to pixels.</summary>
public sealed class WalkMotion
{
    private double _remainder;

    public double Advance(double speedPerSecond, double seconds, double dipPerPixel = 1)
    {
        if (seconds <= 0) return 0;
        _remainder += speedPerSecond * seconds;
        var pixels = Math.Floor(_remainder / dipPerPixel + 1e-9);
        var distance = pixels * dipPerPixel;
        _remainder = Math.Max(0, _remainder - distance);
        return distance;
    }

    public void Reset() => _remainder = 0;
}
