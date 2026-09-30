namespace DesktopPet.Core;

public readonly record struct PetRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public static class PetGeometry
{
    public static double ClampX(double x, PetRect area, double petWidth) => Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - petWidth));
    public static double BottomAlignedTop(PetRect area, double petHeight) => area.Bottom - petHeight;
}
