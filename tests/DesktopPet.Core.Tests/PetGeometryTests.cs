using DesktopPet.Core;
using Xunit;

namespace DesktopPet.Core.Tests;

public sealed class PetGeometryTests
{
    private static readonly PetRect Area = new(0, 0, 1920, 1040);
    private const double PetWidth = 180;

    [Fact]
    public void Step_moves_towards_the_target_by_at_most_one_step()
    {
        var step = PetGeometry.Step(500, 900, 1.1, Area, PetWidth);

        Assert.Equal(501.1, step.X, 5);
        Assert.False(step.Arrived);
    }

    [Fact]
    public void Step_reports_arrival_once_within_the_epsilon()
    {
        // 1.0 px short of the target, epsilon is 1.2, so this tick lands and finishes.
        var step = PetGeometry.Step(899, 900, 1.1, Area, PetWidth);

        Assert.Equal(900, step.X, 5);
        Assert.True(step.Arrived);
    }

    [Fact]
    public void Step_never_leaves_the_working_area_when_the_target_is_off_screen()
    {
        // Walk right repeatedly towards a target beyond the right edge.
        var x = 1800.0;
        for (var i = 0; i < 200; i++)
        {
            var step = PetGeometry.Step(x, 5000, 1.1, Area, PetWidth);
            x = step.X;
            Assert.InRange(x, Area.Left, Area.Right - PetWidth);
        }
    }

    [Fact]
    public void Step_honours_the_deadline_flag()
    {
        // Still far from the target, but the stroll has run out of time.
        var step = PetGeometry.Step(100, 900, 1.1, Area, PetWidth, deadlineReached: true);

        Assert.True(step.Arrived);
    }

    [Fact]
    public void Step_is_clamped_on_a_monitor_left_of_the_primary()
    {
        var left = new PetRect(-1600, 0, 1600, 860);

        Assert.Equal(-1600, PetGeometry.Step(-1600, -5000, 1.1, left, PetWidth).X, 5);
        Assert.Equal(-180.0, PetGeometry.Step(-100, 5000, 1.1, left, PetWidth).X, 5);
    }

    [Fact]
    public void PickStrollTarget_stays_inside_the_area_and_within_range()
    {
        // Offset far beyond the range must be clamped back to +-150, then clamped to the area.
        Assert.Equal(1150, PetGeometry.PickStrollTarget(1000, 150, Area, PetWidth, 9999), 5);
        Assert.Equal(850, PetGeometry.PickStrollTarget(1000, 150, Area, PetWidth, -9999), 5);

        // Near the right edge the target is pulled back so the whole pet stays visible.
        Assert.Equal(1740, PetGeometry.PickStrollTarget(1700, 150, Area, PetWidth, 150), 5);

        // Negative coordinates are respected too.
        var left = new PetRect(-1600, 0, 1600, 860);
        Assert.Equal(-1500, PetGeometry.PickStrollTarget(-1400, 150, left, PetWidth, -100), 5);
    }

    [Fact]
    public void ClampX_is_stable_when_the_pet_is_wider_than_the_area()
    {
        var narrow = new PetRect(0, 0, 100, 800);

        Assert.Equal(0, PetGeometry.ClampX(50, narrow, PetWidth));
        Assert.Equal(0, PetGeometry.ClampX(-50, narrow, PetWidth));
    }
}

public sealed class DipTransformTests
{
    [Fact]
    public void Identity_leaves_coordinates_unchanged()
    {
        var rect = DipTransform.Identity.ToDipRect(0, 0, 1920, 1080);

        Assert.Equal(new PetRect(0, 0, 1920, 1080), rect);
    }

    [Fact]
    public void Scales_both_corners_at_a_fractional_scale_factor()
    {
        // A 2560 x 1600 panel at 150% reports 1707 x 1067 device pixels.
        var rect = new DipTransform(2.0 / 3.0, 2.0 / 3.0).ToDipRect(0, 0, 1707, 1067);

        Assert.Equal(1138.0, rect.Width, 1);
        Assert.Equal(711.3, rect.Height, 1);
    }

    [Fact]
    public void Preserves_a_monitor_positioned_left_of_the_primary()
    {
        // Negative origin must survive conversion; using width-only scaling would lose it.
        var rect = new DipTransform(0.5, 0.5).ToDipRect(-2560, 0, 1920, 1080);

        Assert.Equal(-1280, rect.Left, 5);
        Assert.Equal(960, rect.Width, 5);
        Assert.Equal(540, rect.Height, 5);
    }

    [Fact]
    public void Normalises_a_negative_scale_to_a_positive_rectangle()
    {
        var rect = new DipTransform(-0.5, -0.5).ToDipRect(100, 200, 400, 300);

        Assert.Equal(-250, rect.Left, 5);
        Assert.Equal(-250, rect.Top, 5);
        Assert.Equal(200, rect.Width, 5);
        Assert.Equal(150, rect.Height, 5);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void Degenerate_matrices_fall_back_to_identity(double m11, double m22)
    {
        var transform = DipTransform.FromDeviceToDip(m11, m22);

        Assert.True(transform.IsIdentity);
        Assert.Equal(new PetRect(10, 20, 30, 40), transform.ToDipRect(10, 20, 30, 40));
    }

    [Fact]
    public void Valid_matrix_is_kept()
    {
        var transform = DipTransform.FromDeviceToDip(0.6666666, 0.6666666);

        Assert.False(transform.IsIdentity);
        Assert.Equal(0.6666666, transform.ScaleX, 6);
    }
}
