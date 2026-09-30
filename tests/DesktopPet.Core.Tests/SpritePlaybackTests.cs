using DesktopPet.Core;
using Xunit;

namespace DesktopPet.Core.Tests;

/// <summary>
/// Guards the sprite playback rules. These live in Core because they are pure arithmetic; the WPF
/// layer only supplies the per-state elapsed time.
///
/// The bug these exist to prevent: the renderer used to advance one ever-growing animation clock
/// shared by every state. A one-shot sequence then computed a frame index far beyond its own length,
/// clamped to its last frame, and showed a single still image for the entire state — the turns and
/// the walk-up/walk-down never animated at all. Only the looping walk appeared to work, because
/// wrapping hid the fault. So the time base is per state, and these tests pin that down.
/// </summary>
public sealed class SpritePlaybackTests
{
    /// <summary>Elapsed time, in seconds, at which frame <paramref name="n"/> begins.</summary>
    private static double Frames(int n, int rate = 30) => n / (double)rate;

    [Theory]
    [InlineData(PetState.TurningLeft, 66)]
    [InlineData(PetState.WalkStarting, 62)]
    [InlineData(PetState.WalkStopping, 32)]
    [InlineData(PetState.TurningBack, 57)]
    [InlineData(PetState.Sighing, 122)]
    public void A_one_shot_sequence_holds_its_final_frame(PetState state, int frameCount)
    {
        Assert.False(AnimationTiming.Loops(state));

        var last = frameCount - 1;
        // At its own end it is on the last frame...
        Assert.Equal(last, AnimationTiming.FrameIndex(state, frameCount, Frames(last)));
        // ...and it stays there, however far past the end the clock runs.
        Assert.Equal(last, AnimationTiming.FrameIndex(state, frameCount, Frames(frameCount)));
        Assert.Equal(last, AnimationTiming.FrameIndex(state, frameCount, Frames(frameCount * 10)));
        Assert.Equal(last, AnimationTiming.FrameIndex(state, frameCount, 600));
    }

    [Theory]
    [InlineData(PetState.TurningLeft, 66)]
    [InlineData(PetState.WalkStarting, 62)]
    [InlineData(PetState.WalkStopping, 32)]
    [InlineData(PetState.TurningBack, 57)]
    public void A_one_shot_sequence_actually_advances_through_its_frames(PetState state, int frameCount)
    {
        // Walk the clock across the sequence and confirm every frame index is reached. This is what
        // "the animation plays" means, and what a stuck clock would fail.
        var seen = new HashSet<int>();
        for (var f = 0; f < frameCount; f++) seen.Add(AnimationTiming.FrameIndex(state, frameCount, Frames(f)));

        Assert.Equal(frameCount, seen.Count);
        Assert.Equal(0, AnimationTiming.FrameIndex(state, frameCount, 0));
    }

    [Fact]
    public void A_stuck_clock_would_show_only_the_final_frame()
    {
        // Reproduces the shape of the original fault: if the per-state clock is never reset, a later
        // state is handed a large elapsed time and can only ever render its last frame. Every one of
        // these collapses to a single index.
        const double afterPreviousStates = 12.0;
        foreach (var (state, frameCount) in new[]
                 {
                     (PetState.TurningLeft, 66), (PetState.WalkStarting, 62), (PetState.WalkStopping, 32)
                 })
        {
            Assert.Equal(frameCount - 1,
                AnimationTiming.FrameIndex(state, frameCount, afterPreviousStates));
        }

        // With a per-state clock the same states start from the beginning instead.
        Assert.Equal(0, AnimationTiming.FrameIndex(PetState.TurningLeft, 66, 0));
    }

    [Theory]
    [InlineData(PetState.Idle, true)]
    [InlineData(PetState.WalkingLeft, true)]
    [InlineData(PetState.Sleeping, true)]
    [InlineData(PetState.Sighing, false)]
    [InlineData(PetState.TurningLeft, false)]
    [InlineData(PetState.WalkStarting, false)]
    [InlineData(PetState.WalkStopping, false)]
    [InlineData(PetState.TurningBack, false)]
    public void Looping_is_declared_per_state(PetState state, bool loops)
    {
        Assert.Equal(loops, AnimationTiming.Loops(state));
    }

    [Fact]
    public void A_looping_sequence_wraps_instead_of_clamping()
    {
        const int idleFrames = 122;

        Assert.Equal(0, AnimationTiming.FrameIndex(PetState.Idle, idleFrames, 0));
        Assert.Equal(idleFrames - 1, AnimationTiming.FrameIndex(PetState.Idle, idleFrames, Frames(idleFrames - 1)));
        // A hair past the end of one full pass wraps back to the start. The nudge avoids an exact
        // boundary, where 1/30 is not representable in binary and 4.1 * 30 truncates to 122.
        Assert.Equal(0, AnimationTiming.FrameIndex(PetState.Idle, idleFrames, Frames(idleFrames) + 0.0005));
        Assert.Equal(1, AnimationTiming.FrameIndex(PetState.Idle, idleFrames, Frames(idleFrames + 1) + 0.0005));
    }

    [Fact]
    public void Every_state_plays_at_the_rate_its_duration_assumes()
    {
        // The controller's phase durations are derived from frame counts at 30 fps, so the rate and
        // the count must agree or a phase ends mid-animation or sits frozen on a held frame.
        foreach (var state in new[]
                 {
                     PetState.Idle, PetState.Sighing, PetState.WalkingLeft, PetState.TurningLeft,
                     PetState.WalkStarting, PetState.WalkStopping, PetState.TurningBack
                 })
        {
            Assert.Equal(30, AnimationTiming.FrameRateFor(state));
        }

        // A sequence's duration is what the controller schedules against.
        Assert.Equal(66 / 30.0, AnimationTiming.Duration(PetState.TurningLeft, 66), 6);
        Assert.Equal(93 / 30.0, AnimationTiming.Duration(PetState.WalkingLeft, 93), 6);
    }

    [Fact]
    public void An_empty_sequence_reports_no_frame_rather_than_throwing()
    {
        Assert.Equal(-1, AnimationTiming.FrameIndex(PetState.Dragging, 0, 0));
    }
}
