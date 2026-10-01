using DesktopPet.Core;
using Xunit;

namespace DesktopPet.Core.Tests;

public sealed class StrollScheduleTests
{
    [Fact]
    public void Next_delay_is_between_thirty_and_sixty_seconds()
    {
        var from = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var random = new Random(1234);

        for (var i = 0; i < 100; i++)
        {
            var next = StrollSchedule.NextWalkTime(from, random);
            var seconds = (next - from).TotalSeconds;

            Assert.InRange(seconds, StrollSchedule.MinimumIdleSeconds, StrollSchedule.MaximumIdleSeconds);
        }
    }
}
