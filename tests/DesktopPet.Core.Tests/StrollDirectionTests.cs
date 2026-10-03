using DesktopPet.Core;
using Xunit;

namespace DesktopPet.Core.Tests;

public sealed class StrollDirectionTests
{
    private const double PetWidth = 180;

    [Theory]
    [InlineData(0, 1000, 250, 25)]
    [InlineData(0, 1000, 500, 50)]
    [InlineData(0, 1000, 750, 75)]
    [InlineData(-1600, 1000, 250, 25)]
    [InlineData(2000, 1000, 750, 75)]
    [InlineData(0, 2000, 500, 25)]
    public void Position_weights_the_direction_towards_the_monitor_centre(
        double origin, double travelWidth, double offset, int expectedLeftChoices)
    {
        var area = new PetRect(origin, 0, travelWidth + PetWidth, 1000);
        var leftChoices = 0;

        // Cover equal-sized portions of the random range without relying on a lucky random seed.
        for (var i = 0; i < 100; i++)
        {
            var direction = PetGeometry.PickStrollDirection(origin + offset, area, PetWidth, (i + 0.5) / 100);
            Assert.NotNull(direction);
            if (direction == true) leftChoices++;
        }

        Assert.Equal(expectedLeftChoices, leftChoices);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(988, 0.9999, true)]
    [InlineData(1000, 0.9999, true)]
    [InlineData(-100, 0, false)]
    [InlineData(1100, 0.9999, true)]
    public void An_edge_always_selects_the_direction_with_room(
        double x, double sample, bool expectedLeft)
    {
        var area = new PetRect(0, 0, 1180, 1000);

        Assert.Equal(expectedLeft, PetGeometry.PickStrollDirection(x, area, PetWidth, sample));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(180, 0)]
    [InlineData(200, 10)]
    [InlineData(204, 12)]
    public void No_direction_is_selected_when_neither_side_has_room(double width, double x)
    {
        var area = new PetRect(0, 0, width, 1000);

        Assert.Null(PetGeometry.PickStrollDirection(x, area, PetWidth, 0));
        Assert.Null(PetGeometry.PickStrollDirection(x, area, PetWidth, 0.9999));
    }
}
