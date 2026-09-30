namespace DesktopPet.Core;

/// <summary>
/// Converts between physical device pixels (what Windows Forms <c>Screen</c> reports) and
/// WPF device-independent pixels (what <c>Window.Left</c>/<c>Top</c> use).
///
/// Each monitor has its own scale factor, so the correct transform depends on which monitor
/// the coordinates came from. Applying the window's current-monitor scale to a *different*
/// monitor's rectangle produces the wrong offset on mixed-DPI desktops.
/// </summary>
public readonly record struct DipTransform(double ScaleX, double ScaleY)
{
    /// <summary>Builds a transform from a WPF <c>CompositionTarget.TransformFromDevice</c> matrix.</summary>
    public static DipTransform FromDeviceToDip(double m11, double m22)
    {
        if (!double.IsFinite(m11) || !double.IsFinite(m22) || m11 <= 0 || m22 <= 0)
            return Identity;
        return new DipTransform(m11, m22);
    }

    public static DipTransform Identity { get; } = new(1, 1);

    public bool IsIdentity => ScaleX == 1 && ScaleY == 1;

    public (double X, double Y) ToDip(double x, double y) => (x * ScaleX, y * ScaleY);

    /// <summary>
    /// Converts a device-pixel rectangle to DIP. Both corners are scaled, so the result stays
    /// correct for monitors positioned at negative coordinates (left of the primary display)
    /// and for negative scale factors.
    /// </summary>
    public PetRect ToDipRect(double left, double top, double width, double height)
    {
        var (x0, y0) = ToDip(left, top);
        var (x1, y1) = ToDip(left + width, top + height);
        var dipLeft = Math.Min(x0, x1);
        var dipTop = Math.Min(y0, y1);
        return new PetRect(dipLeft, dipTop, Math.Abs(x1 - x0), Math.Abs(y1 - y0));
    }
}
