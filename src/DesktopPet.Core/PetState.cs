namespace DesktopPet.Core;

public enum PetState
{
    Idle,
    WalkingLeft,
    WalkingRight,
    Responding,
    Sleeping,
    Dragging,
    Sighing,

    // The leftward stroll: one entry per sprite sequence. Only WalkingLeft travels; the two turns
    // and the walk-down are acted in place.
    TurningLeft,
    WalkStarting,
    WalkStopping,
    TurningBack,

    // The rightward stroll. It has no repeat knob: its walk module is a one-shot that already ends
    // facing the viewer, so a right stroll is always its own fixed length.
    TurningRight,
    StandingRight
}

/// <summary>
/// Where a stroll currently is. Each stroll is one continuous piece of acting cut into sprite
/// sequences; which of them travel across the screen depends on the direction.
/// </summary>
public enum StrollPhase
{
    None,

    // Leftward: turn out, play the supplied walking pass once, then turn back to the viewer.
    TurningLeft,
    Starting,
    Walking,
    Stopping,
    TurningBack,

    // Rightward: turn out, walk (one shot, already ends facing the viewer), settle.
    TurningRight,
    WalkingRight,
    StandingRight
}
