using DesktopPet.Core;
using Xunit;

namespace DesktopPet.Core.Tests;

/// <summary>
/// The supplied rightward stroll plays three clips once. Its opening turn and final settle are acted
/// in place, and only the middle walking clip travels.
/// </summary>
public sealed class RightStrollTests
{
    private static TimeSpan Frames(int n) => TimeSpan.FromSeconds(n / 30.0);

    private static PetController StartRight()
    {
        var controller = new PetController();
        Assert.True(controller.TryStartStroll(goLeft: false));
        return controller;
    }

    [Fact]
    public void A_right_stroll_runs_through_its_three_phases_in_order()
    {
        var controller = StartRight();
        var seen = new List<StrollPhase> { controller.Phase };

        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 4000; i++)
        {
            controller.Advance(tick);
            if (seen[^1] != controller.Phase) seen.Add(controller.Phase);
            if (!controller.IsStrolling && seen.Count > 1) break;
        }

        Assert.Equal(
            new[] { StrollPhase.TurningRight, StrollPhase.WalkingRight, StrollPhase.StandingRight, StrollPhase.None },
            seen);
    }

    [Fact]
    public void The_turn_and_settle_are_acted_in_place()
    {
        var controller = StartRight();
        Assert.Equal(StrollPhase.TurningRight, controller.Phase);
        Assert.False(controller.IsMoving);

        while (controller.Phase == StrollPhase.TurningRight) controller.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(StrollPhase.WalkingRight, controller.Phase);
        Assert.True(controller.IsMoving);

        while (controller.Phase == StrollPhase.WalkingRight) controller.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(StrollPhase.StandingRight, controller.Phase);
        Assert.False(controller.IsMoving);
    }

    [Theory]
    [InlineData(StrollPhase.TurningRight, 42, StrollPhase.WalkingRight, false)]
    [InlineData(StrollPhase.WalkingRight, 215, StrollPhase.StandingRight, true)]
    [InlineData(StrollPhase.StandingRight, 45, StrollPhase.None, false)]
    public void Each_supplied_clip_finishes_before_the_next_phase(
        StrollPhase phase, int frames, StrollPhase next, bool moves)
    {
        var controller = StartRight();
        while (controller.Phase != phase) controller.Advance(TimeSpan.FromMilliseconds(50));

        var finalMillisecond = TimeSpan.FromMilliseconds(1);
        controller.Advance(Frames(frames) - finalMillisecond);
        Assert.Equal(phase, controller.Phase);
        Assert.Equal(moves, controller.IsMoving);

        controller.Advance(finalMillisecond);
        Assert.Equal(next, controller.Phase);
    }

    [Fact]
    public void A_right_stroll_is_one_fixed_pass_whatever_the_repeat_count()
    {
        // The walk module cannot be repeated, so the repeat knob must not lengthen a right stroll.
        static TimeSpan Duration(int repeats)
        {
            var c = new PetController { StrollRepeatCount = repeats };
            Assert.True(c.TryStartStroll(goLeft: false));
            return c.RightStrollDuration;
        }

        Assert.Equal(Frames(42 + 215 + 45), Duration(1));
        Assert.Equal(Duration(1), Duration(2));
        Assert.Equal(Duration(1), Duration(5));

        // And a zero repeat count must not block it, unlike the leftward stroll.
        var blocked = new PetController { StrollRepeatCount = 0 };
        Assert.True(blocked.TryStartStroll(goLeft: false));
    }

    [Fact]
    public void A_right_stroll_travels_for_its_walk_module_only()
    {
        var controller = StartRight();
        var moving = TimeSpan.Zero;
        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 4000 && controller.IsStrolling; i++)
        {
            if (controller.IsMoving) moving += tick;
            controller.Advance(tick);
        }

        // The opening turn and final settle are acted in place; only the walk module travels.
        var expected = Frames(215);
        Assert.True(Math.Abs((moving - expected).TotalSeconds) <= 0.1,
            $"travelling window was {moving.TotalSeconds:F2}s, expected about {expected.TotalSeconds:F2}s");
    }

    [Fact]
    public void A_right_stroll_ends_at_idle_facing_the_viewer()
    {
        var controller = StartRight();
        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 4000 && controller.IsStrolling; i++) controller.Advance(tick);

        Assert.False(controller.IsStrolling);
        Assert.Equal(StrollPhase.None, controller.Phase);
        Assert.Equal(PetState.Idle, controller.State);
    }

    [Fact]
    public void Direction_is_reported_while_strolling()
    {
        var right = StartRight();
        Assert.True(right.IsStrollingRight);
        Assert.False(right.IsStrollingLeft);

        var left = new PetController();
        Assert.True(left.TryStartStroll(goLeft: true));
        Assert.True(left.IsStrollingLeft);
        Assert.False(left.IsStrollingRight);
    }

    [Fact]
    public void The_right_stroll_obeys_the_same_start_guards_as_the_left()
    {
        Assert.False(new PetController { IsPaused = true }.TryStartStroll(goLeft: false));

        var sleeping = new PetController();
        sleeping.ToggleManualSleep();
        Assert.False(sleeping.TryStartStroll(goLeft: false));

        var auto = new PetController();
        auto.Advance(TimeSpan.FromMinutes(6));
        Assert.False(auto.TryStartStroll(goLeft: false));

        var dragging = new PetController();
        dragging.BeginDrag();
        Assert.False(dragging.TryStartStroll(goLeft: false));

        var strolling = StartRight();
        Assert.False(strolling.TryStartStroll(goLeft: false));
    }

    [Fact]
    public void Each_right_phase_maps_to_its_own_sprite_state()
    {
        var controller = StartRight();
        var map = new Dictionary<StrollPhase, PetState>();
        var tick = TimeSpan.FromMilliseconds(50);
        for (var i = 0; i < 4000 && controller.IsStrolling; i++)
        {
            if (controller.IsStrolling) map[controller.Phase] = controller.State;
            controller.Advance(tick);
        }

        Assert.Equal(PetState.TurningRight, map[StrollPhase.TurningRight]);
        Assert.Equal(PetState.WalkingRight, map[StrollPhase.WalkingRight]);
        Assert.Equal(PetState.StandingRight, map[StrollPhase.StandingRight]);
    }
}

/// <summary>Playback rules for the rightward walk, which is deliberately not a loop.</summary>
public sealed class RightWalkPlaybackTests
{
    [Fact]
    public void The_right_walk_does_not_loop()
    {
        // Each supplied clip is one complete pass, so wrapping would restart its action mid-stroll.
        Assert.False(AnimationTiming.Loops(PetState.WalkingRight));
        Assert.False(AnimationTiming.Loops(PetState.TurningRight));
        Assert.False(AnimationTiming.Loops(PetState.StandingRight));
    }

    [Fact]
    public void All_three_right_states_play_at_30_fps()
    {
        Assert.Equal(30, AnimationTiming.FrameRateFor(PetState.TurningRight));
        Assert.Equal(30, AnimationTiming.FrameRateFor(PetState.WalkingRight));
        Assert.Equal(30, AnimationTiming.FrameRateFor(PetState.StandingRight));
    }

    [Theory]
    [InlineData(PetState.TurningRight, 42)]
    [InlineData(PetState.WalkingRight, 215)]
    [InlineData(PetState.StandingRight, 45)]
    public void Each_right_sequence_advances_through_its_frames_and_holds_the_last(PetState state, int frames)
    {
        // Sample from the middle of each frame's slot, which is where a playing animation actually
        // spends its time. Sampling exactly on a boundary is unreliable because the elapsed time comes
        // from a TimeSpan rounded to whole milliseconds, so a boundary instant can land a hair short
        // and repeat the previous frame. Midpoints prove all `frames` frames are reachable.
        static double Mid(int frame) => TimeSpan.FromSeconds(frame / 30.0).TotalSeconds + 0.5 / 30.0;

        var seen = new HashSet<int>();
        for (var f = 0; f < frames; f++) seen.Add(AnimationTiming.FrameIndex(state, frames, Mid(f)));
        Assert.Equal(frames, seen.Count);

        // The sequence holds its final frame at and past its own end.
        var end = TimeSpan.FromSeconds(frames / 30.0).TotalSeconds;
        Assert.Equal(frames - 1, AnimationTiming.FrameIndex(state, frames, end));
        Assert.Equal(frames - 1, AnimationTiming.FrameIndex(state, frames, 600));
    }
}
