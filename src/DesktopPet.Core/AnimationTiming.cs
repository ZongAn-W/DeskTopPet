namespace DesktopPet.Core;

/// <summary>
/// Playback rules for the sprite sequences: how fast each state runs, and whether it repeats or
/// holds its final frame. This is pure arithmetic with no WPF dependency, so it is unit-testable and
/// is the single source of truth the WPF loader builds its frame lists from.
/// </summary>
public static class AnimationTiming
{
    /// <summary>Idle, the sigh and the walk set are authored at 30 fps.</summary>
    public const int IdleFrameRate = 30;

    /// <summary>The older placeholder-driven states are authored at 8 fps.</summary>
    public const int WalkFrameRate = 8;

    /// <summary>
    /// Whether each state repeats or holds its final frame.
    /// </summary>
    /// <remarks>
    /// Holding matters: a one-shot sequence that looped would jump from its finished pose back to
    /// its first frame while the controller still believed the action was running.
    /// </remarks>
    public static bool Loops(PetState state) => state switch
    {
        PetState.Idle => true,
        PetState.WalkingLeft => true,
        PetState.Sleeping => true,
        // WalkingRight is a one-shot: the clip already ends facing the viewer, so it must not wrap.
        _ => false
    };

    /// <summary>Playback rate for a state, and therefore how long its sequence takes.</summary>
    public static int FrameRateFor(PetState state) => state switch
    {
        PetState.Idle => IdleFrameRate,
        PetState.Sighing => IdleFrameRate,
        PetState.WalkingLeft => IdleFrameRate,
        PetState.TurningLeft => IdleFrameRate,
        PetState.WalkStarting => IdleFrameRate,
        PetState.WalkStopping => IdleFrameRate,
        PetState.TurningBack => IdleFrameRate,
        PetState.TurningRight => IdleFrameRate,
        PetState.WalkingRight => IdleFrameRate,
        PetState.StandingRight => IdleFrameRate,
        _ => WalkFrameRate
    };

    /// <summary>
    /// Which frame index a sequence shows after <paramref name="seconds"/> of *that state's own*
    /// elapsed time. Callers must pass time since the state began, not since the app started: an
    /// ever-growing shared clock makes a one-shot sequence compute an index past its end, clamp to
    /// its final frame, and show a single still image for the whole state.
    /// </summary>
    public static int FrameIndex(PetState state, int frameCount, double seconds)
    {
        if (frameCount <= 0) return -1;
        var index = (int)(seconds * FrameRateFor(state));
        return Loops(state) ? index % frameCount : Math.Clamp(index, 0, frameCount - 1);
    }

    /// <summary>How long a full pass through a sequence takes, in seconds.</summary>
    public static double Duration(PetState state, int frameCount) => frameCount / (double)FrameRateFor(state);
}
