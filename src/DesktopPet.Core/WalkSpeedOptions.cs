namespace DesktopPet.Core;

public static class WalkSpeedOptions
{
    public const double Default = 2.0;

    public static IReadOnlyList<double> Values { get; } = Array.AsReadOnly(new[]
    {
        1.0,
        1.5,
        2.0,
        3.0
    });

    public static double Normalize(double speed) => Values.Contains(speed) ? speed : Default;
}
