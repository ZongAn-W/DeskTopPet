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

    // The rightward stroll plays three supplied clips once; only WalkingRight travels.
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

    // Rightward: turn out in place, walk once, then turn back in place.
    TurningRight,
    WalkingRight,
    StandingRight
}
