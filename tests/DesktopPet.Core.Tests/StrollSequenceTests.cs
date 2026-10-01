using DesktopPet.Core;
using Xunit;

namespace DesktopPet.Core.Tests;

/// <summary>
/// The three-phase leftward stroll: fixed opening clip, travelling middle clip, fixed closing clip.
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
    public void A_stroll_runs_through_the_three_phases_in_order()
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
                StrollPhase.TurningLeft, StrollPhase.Walking, StrollPhase.TurningBack, StrollPhase.None
            },
            seen);
    }

    [Theory]
    [InlineData(StrollPhase.TurningLeft)]
    [InlineData(StrollPhase.TurningBack)]
    public void The_turns_and_the_walk_down_are_acted_in_place(StrollPhase phase)
    {
        var controller = StartStrolling();
        while (controller.Phase != phase) { controller.Advance(TimeSpan.FromMilliseconds(50)); }

        Assert.False(controller.IsMoving);
        Assert.True(controller.IsStrolling);
    }

    [Theory]
    [InlineData(StrollPhase.Walking)]
    public void The_travelling_phase_moves_the_pet(StrollPhase phase)
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

        Assert.True(moving[StrollPhase.Walking]);
        Assert.False(moving[StrollPhase.TurningLeft]);
        Assert.False(moving[StrollPhase.TurningBack]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Walk_repeat_count_does_not_repeat_the_walking_clip(int repeats)
    {
        var controller = StartStrolling(repeats);

        AssertClose(Frames(29 + 175 + 94), controller.StrollDuration);

        // Measure the travelling window by ticking through the stroll. The legacy setting remains
        // accepted for compatibility, but the supplied walking video is a single complete pass.
        var moving = TimeSpan.Zero;
        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 4000 && controller.IsStrolling; i++)
        {
            if (controller.IsMoving) moving += tick;
            controller.Advance(tick);
        }

        var expected = TimeSpan.FromSeconds(175 / 30.0);
        Assert.True(Math.Abs((moving - expected).TotalSeconds) <= 0.1,
            $"travelling window was {moving.TotalSeconds:F2}s, expected about {expected.TotalSeconds:F2}s");
    }

    [Fact]
    public void Legacy_repeat_settings_produce_the_same_travel_distance()
    {
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

        Assert.Equal(once, twice, precision: 1);
        Assert.Equal(once, four, precision: 1);
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
        Assert.Equal(PetState.WalkingLeft, map[StrollPhase.Walking]);
        Assert.Equal(PetState.TurningBack, map[StrollPhase.TurningBack]);
    }

    [Fact]
    public void Zero_repeats_is_refused_rather_than_walking_nowhere()
    {
        var controller = new PetController { StrollRepeatCount = 0 };

        Assert.False(controller.TryStartStroll());
    }
}
