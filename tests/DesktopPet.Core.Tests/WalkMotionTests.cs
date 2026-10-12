using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class WalkMotionTests
{
    [Theory]
    [InlineData(30, 1)]
    [InlineData(60, 1)]
    [InlineData(144, 1)]
    [InlineData(60, 0.8)]
    [InlineData(144, 2.0 / 3)]
    public void Travel_is_independent_of_refresh_rate_and_display_scale(int frameRate, double dipPerPixel)
    {
        var motion = new WalkMotion();
        var distance = 0.0;
        for (var frame = 0; frame < frameRate * 5; frame++)
        {
            var step = motion.Advance(WalkSpeedOptions.PerSecond(1.5), 1.0 / frameRate, dipPerPixel);
            Assert.Equal(Math.Round(step / dipPerPixel), step / dipPerPixel, 8);
            distance += step;
        }

        Assert.Equal(120, distance, 6);
    }

    [Fact]
    public void Irregular_intervals_preserve_the_elapsed_distance()
    {
        var motion = new WalkMotion();
        var intervals = new[] { 0.010, 0.025, 0.005, 0.060, 0.012, 0.008, 0.030, 0.050 };
        var distance = 0.0;
        for (var pass = 0; pass < 25; pass++)
            foreach (var interval in intervals)
                distance += motion.Advance(WalkSpeedOptions.PerSecond(2), interval);

        Assert.Equal(160, distance, 6);
    }

    [Fact]
    public void Reset_discards_the_old_directions_subpixel_remainder()
    {
        var motion = new WalkMotion();
        Assert.Equal(0, motion.Advance(16, 0.05));
        motion.Reset();
        Assert.Equal(0, motion.Advance(16, 0.05));
        Assert.Equal(1, motion.Advance(16, 0.05));
    }

    [Fact]
    public void Duplicate_presentation_times_do_not_move_the_pet()
    {
        var motion = new WalkMotion();
        Assert.Equal(0, motion.Advance(32, 0));
        Assert.Equal(0, motion.Advance(32, -1));
        Assert.Equal(1, motion.Advance(32, 1.0 / 32));
    }
}
