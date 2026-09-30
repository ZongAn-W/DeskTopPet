using DesktopPet.Core;
using Xunit;

namespace DesktopPet.Core.Tests;

/// <summary>
/// The five-phase leftward stroll. Only the two turns are acted in place; the walk-up, the walk
/// cycle and the walk-down all travel.
/// </summary>
public sealed class StrollSequenceTests
{
    /// <summary>One 30 fps frame, the unit every phase duration is built from.</summary>
    private static TimeSpan Frames(int n) => TimeSpan.FromSeconds(n / 30.0);

    /// <summary>
    /// Summing five TimeSpans rounds each term, so allow a few ticks of drift instead of demanding
    /// bit-exact equality.
    /// </summary>
    private static void AssertClose(TimeSpan expected, TimeSpan actual, int toleranceTicks = 5)
    {
        Assert.True(Math.Abs((expected - actual).Ticks) <= toleranceTicks,
            $"expected about {expected}, got {actual}");
    }

    private static PetController StartStrolling(int repeats = 2)
    {
        var controller = new PetController { StrollRepeatCount = repeats };
        Assert.True(controller.TryStartStroll());
        return controller;
    }

    /// <summary>Advances in 50 ms ticks, the interval the real dispatcher timer uses.</summary>
    private static PetController Run(PetController controller, TimeSpan total)
    {
        var tick = TimeSpan.FromMilliseconds(50);
        for (var t = TimeSpan.Zero; t < total; t += tick) controller.Advance(tick);
        return controller;
    }

    [Fact]
    public void A_stroll_runs_through_all_five_phases_in_order()
    {
        var controller = StartStrolling();
        var seen = new List<StrollPhase> { controller.Phase };

        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 2000; i++)
        {
            controller.Advance(tick);
            if (seen[^1] != controller.Phase) seen.Add(controller.Phase);
            if (!controller.IsStrolling && seen.Count > 1) break;
        }

        Assert.Equal(
            new[]
            {
                StrollPhase.TurningLeft, StrollPhase.Starting, StrollPhase.Walking,
                StrollPhase.Stopping, StrollPhase.TurningBack, StrollPhase.None
            },
            seen);
    }

    [Theory]
    [InlineData(StrollPhase.TurningLeft)]
    [InlineData(StrollPhase.Stopping)]
    [InlineData(StrollPhase.TurningBack)]
    public void The_turns_and_the_walk_down_are_acted_in_place(StrollPhase phase)
    {
        var controller = StartStrolling();
        while (controller.Phase != phase) { controller.Advance(TimeSpan.FromMilliseconds(50)); }

        Assert.False(controller.IsMoving);
        Assert.True(controller.IsStrolling);
    }

    [Theory]
    [InlineData(StrollPhase.Starting)]
    [InlineData(StrollPhase.Walking)]
    public void The_two_travelling_phases_move_the_pet(StrollPhase phase)
    {
        var controller = StartStrolling();
        while (controller.Phase != phase) controller.Advance(TimeSpan.FromMilliseconds(50));

        Assert.True(controller.IsMoving);
    }

    [Fact]
    public void Exactly_the_two_travelling_phases_move()
    {
        var controller = StartStrolling();
        var moving = new Dictionary<StrollPhase, bool>();
        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 4000 && controller.IsStrolling; i++)
        {
            if (controller.IsStrolling) moving[controller.Phase] = controller.IsMoving;
            controller.Advance(tick);
        }

        Assert.True(moving[StrollPhase.Starting]);
        Assert.True(moving[StrollPhase.Walking]);
        Assert.False(moving[StrollPhase.TurningLeft]);
        Assert.False(moving[StrollPhase.Stopping]);
        Assert.False(moving[StrollPhase.TurningBack]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Walk_repeat_count_scales_the_walking_phase_and_the_total(int repeats)
    {
        var controller = StartStrolling(repeats);

        AssertClose(Frames(66 + 62 + repeats * 93 + 32 + 57), controller.StrollDuration);

        // Measure the travelling window by ticking through the stroll: the walk-up plus the repeats.
        var moving = TimeSpan.Zero;
        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 4000 && controller.IsStrolling; i++)
        {
            if (controller.IsMoving) moving += tick;
            controller.Advance(tick);
        }

        var expected = TimeSpan.FromSeconds((62 + repeats * 93) / 30.0);
        Assert.True(Math.Abs((moving - expected).TotalSeconds) <= 0.1,
            $"travelling window was {moving.TotalSeconds:F2}s, expected about {expected.TotalSeconds:F2}s");
    }

    [Fact]
    public void Repeating_the_walk_cycle_still_increases_the_travel_distance()
    {
        // The pet moves at a fixed speed for the whole travelling window, so distance grows with the
        // repeat count. The walk-up adds a constant on top, so this is an increase, not a doubling.
        static double TravelSeconds(int repeats)
        {
            var c = StartStrolling(repeats);
            var tick = TimeSpan.FromMilliseconds(50);
            double moving = 0;
            for (var i = 0; i < 6000 && c.IsStrolling; i++)
            {
                if (c.IsMoving) moving += tick.TotalSeconds;
                c.Advance(tick);
            }
            return moving;
        }

        var once = TravelSeconds(1);
        var twice = TravelSeconds(2);
        var four = TravelSeconds(4);

        Assert.True(twice > once, $"1x={once:F2}s 2x={twice:F2}s");
        Assert.True(four > twice, $"2x={twice:F2}s 4x={four:F2}s");
        // One extra cycle is worth exactly one walk cycle of travel, whatever the fixed overhead is.
        Assert.True(Math.Abs((twice - once) - 93 / 30.0) < 0.1, $"step was {twice - once:F2}s");
        Assert.True(Math.Abs((four - twice) - 2 * 93 / 30.0) < 0.1, $"step was {four - twice:F2}s");
    }

    [Fact]
    public void A_stroll_ends_back_at_idle_facing_the_viewer()
    {
        var controller = Run(StartStrolling(), TimeSpan.FromSeconds(30));

        Assert.False(controller.IsStrolling);
        Assert.Equal(StrollPhase.None, controller.Phase);
        Assert.Equal(PetState.Idle, controller.State);
    }

    [Fact]
    public void A_stroll_cannot_start_while_asleep_paused_dragging_or_already_strolling()
    {
        Assert.False(new PetController { IsPaused = true }.TryStartStroll());

        var sleeping = new PetController();
        sleeping.ToggleManualSleep();
        Assert.False(sleeping.TryStartStroll());

        var auto = new PetController();
        auto.Advance(TimeSpan.FromMinutes(6));
        Assert.False(auto.TryStartStroll());

        var dragging = new PetController();
        dragging.BeginDrag();
        Assert.False(dragging.TryStartStroll());

        var strolling = StartStrolling();
        Assert.False(strolling.TryStartStroll());
    }

    [Fact]
    public void A_stroll_holds_the_pet_awake_for_its_whole_length()
    {
        // A stroll is longer than nothing but shorter than the five-minute inactivity limit, so tick
        // through it and check the pet never auto-sleeps while it is still acting. (Ticking for six
        // minutes would legitimately put her to sleep *after* the stroll finished.)
        var controller = StartStrolling();

        var tick = TimeSpan.FromMilliseconds(50);
        while (controller.IsStrolling)
        {
            controller.Advance(tick);
            Assert.False(controller.IsAutoSleeping);
            Assert.NotEqual(PetState.Sleeping, controller.State);
        }

        Assert.Equal(PetState.Idle, controller.State);
    }

    [Fact]
    public void Pausing_mid_stroll_drops_the_pet_back_to_idle()
    {
        var controller = StartStrolling();
        while (controller.Phase != StrollPhase.Walking) controller.Advance(TimeSpan.FromMilliseconds(50));

        controller.StopWalking();

        Assert.False(controller.IsStrolling);
        Assert.Equal(PetState.Idle, controller.State);
    }

    [Fact]
    public void Clicking_mid_stroll_interrupts_it_with_the_sigh()
    {
        var controller = StartStrolling();
        while (controller.Phase != StrollPhase.Walking) controller.Advance(TimeSpan.FromMilliseconds(50));

        controller.Click();

        Assert.False(controller.IsStrolling);
        Assert.Equal(PetState.Sighing, controller.State);
    }

    [Fact]
    public void SetWalking_cannot_hijack_a_stroll_in_progress()
    {
        var controller = StartStrolling();
        while (controller.Phase != StrollPhase.Walking) controller.Advance(TimeSpan.FromMilliseconds(50));

        controller.SetWalking(right: false);

        Assert.Equal(PetState.WalkingLeft, controller.State);
        Assert.Equal(StrollPhase.Walking, controller.Phase);
    }

    [Fact]
    public void Each_phase_maps_to_its_own_sprite_state()
    {
        var controller = StartStrolling();
        var map = new Dictionary<StrollPhase, PetState>();
        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 4000 && controller.IsStrolling; i++)
        {
            if (controller.IsStrolling) map[controller.Phase] = controller.State;
            controller.Advance(tick);
        }

        Assert.Equal(PetState.TurningLeft, map[StrollPhase.TurningLeft]);
        Assert.Equal(PetState.WalkStarting, map[StrollPhase.Starting]);
        Assert.Equal(PetState.WalkingLeft, map[StrollPhase.Walking]);
        Assert.Equal(PetState.WalkStopping, map[StrollPhase.Stopping]);
        Assert.Equal(PetState.TurningBack, map[StrollPhase.TurningBack]);
    }

    [Fact]
    public void Zero_repeats_is_refused_rather_than_walking_nowhere()
    {
        var controller = new PetController { StrollRepeatCount = 0 };

        Assert.False(controller.TryStartStroll());
    }
}
