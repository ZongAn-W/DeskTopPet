namespace DesktopPet.Core;

public sealed class PetController
{
    private static readonly TimeSpan AutoSleepAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResponseDuration = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// How long the sigh animation runs. This must match the spirit of the sprite: the sigh is
    /// 122 frames played at 30 fps, so it lasts about 4.07 seconds. If the frame art or its rate
    /// changes, update this too, or the pet will snap back to idle mid-animation.
    /// </summary>
    private static readonly TimeSpan SighDuration = TimeSpan.FromSeconds(122.0 / 30.0);

    // --- leftward stroll timing -----------------------------------------------------------------
    // Every sequence is played at 30 fps, so a phase lasts (frame count / 30) seconds. These frame
    // counts must stay in step with the art in Assets/Character; a mismatch shows up as the pet
    // snapping back to idle mid-animation, or standing frozen on a held final frame.

    private const int TurnLeftFrames = 66;   // turn_left
    private const int WalkStartFrames = 62;  // walk_start
    private const int WalkCycleFrames = 93;  // walk_left, one repetition
    private const int WalkStopFrames = 32;   // walk_stop
    private const int TurnBackFrames = 57;   // turn_back

    // Rightward. WalkRight is a one-shot: it already ends facing the viewer, so it is never repeated.
    private const int TurnRightFrames = 95;  // turn_right
    private const int WalkRightFrames = 159; // walk_right
    private const int StandRightFrames = 18; // stand_right

    /// <summary>Length of a sprite sequence, derived from its frame count at 30 fps.</summary>
    private static TimeSpan Frames(long count) => TimeSpan.FromSeconds(count / 30.0);

    private TimeSpan _sinceInteraction;
    private TimeSpan _stateElapsed;

    public PetController()
    {
        StrollRepeatCount = DefaultStrollRepeatCount;
    }

    /// <summary>
    /// How many times the walk cycle is played per stroll. This is the single knob for how far the
    /// pet travels: distance = repeat count x one cycle. The pet moves at a constant speed while
    /// <see cref="StrollPhase.Walking"/>, so doubling this doubles the distance.
    /// </summary>
    public const int DefaultStrollRepeatCount = 2;

    public int StrollRepeatCount { get; set; }

    public PetState State { get; private set; } = PetState.Idle;
    public bool IsPaused { get; set; }
    public bool IsManualSleeping { get; private set; }
    public bool IsAutoSleeping { get; private set; }

    /// <summary>Which leg of the current stroll is playing, or <see cref="StrollPhase.None"/>.</summary>
    public StrollPhase Phase { get; private set; } = StrollPhase.None;

    public bool IsStrolling => Phase != StrollPhase.None;

    /// <summary>True while a stroll is heading left, which is the direction with a repeat knob.</summary>
    public bool IsStrollingLeft => Phase
        is StrollPhase.TurningLeft or StrollPhase.Starting or StrollPhase.Walking
        or StrollPhase.Stopping or StrollPhase.TurningBack;

    /// <summary>True while a stroll is heading right.</summary>
    public bool IsStrollingRight => Phase
        is StrollPhase.TurningRight or StrollPhase.WalkingRight or StrollPhase.StandingRight;

    /// <summary>
    /// True while the pet should be travelling across the screen.
    /// </summary>
    /// <remarks>
    /// Leftward that is the walk-up and the walk cycle; the two turns and the walk-down are acted in
    /// place. Rightward only the walk module travels; the opening turn and final settle are acted in
    /// place.
    /// </remarks>
    public bool IsMoving => Phase
        is StrollPhase.Starting or StrollPhase.Walking
        or StrollPhase.WalkingRight;

    /// <summary>One repetition of the leftward walk cycle.</summary>
    private TimeSpan OneWalkCycle => Frames(WalkCycleFrames * Math.Max(1, StrollRepeatCount));

    /// <summary>Total wall-clock length of one complete leftward stroll.</summary>
    public TimeSpan StrollDuration =>
        Frames(TurnLeftFrames) + Frames(WalkStartFrames) + OneWalkCycle
        + Frames(WalkStopFrames) + Frames(TurnBackFrames);

    /// <summary>
    /// Total wall-clock length of one complete rightward stroll. It has no repeat knob: the walk
    /// module is a one-shot, so a right stroll is always exactly this long.
    /// </summary>
    public TimeSpan RightStrollDuration =>
        Frames(TurnRightFrames) + Frames(WalkRightFrames) + Frames(StandRightFrames);

    /// <summary>Duration of a timed one-shot state, or <see cref="TimeSpan.Zero"/> if it has none.</summary>
    private static TimeSpan DurationOf(PetState state) => state switch
    {
        PetState.Responding => ResponseDuration,
        PetState.Sighing => SighDuration,
        _ => TimeSpan.Zero
    };

    /// <summary>Length of a stroll phase, or <see cref="TimeSpan.Zero"/> when not strolling.</summary>
    private TimeSpan PhaseDurationOf(StrollPhase phase) => phase switch
    {
        StrollPhase.TurningLeft => Frames(TurnLeftFrames),
        StrollPhase.Starting => Frames(WalkStartFrames),
        StrollPhase.Walking => OneWalkCycle,
        StrollPhase.Stopping => Frames(WalkStopFrames),
        StrollPhase.TurningBack => Frames(TurnBackFrames),
        StrollPhase.TurningRight => Frames(TurnRightFrames),
        StrollPhase.WalkingRight => Frames(WalkRightFrames),
        StrollPhase.StandingRight => Frames(StandRightFrames),
        _ => TimeSpan.Zero
    };

    /// <summary>The phase that follows <paramref name="phase"/>, or None when the stroll is over.</summary>
    private static StrollPhase NextPhase(StrollPhase phase) => phase switch
    {
        StrollPhase.TurningLeft => StrollPhase.Starting,
        StrollPhase.Starting => StrollPhase.Walking,
        StrollPhase.Walking => StrollPhase.Stopping,
        StrollPhase.Stopping => StrollPhase.TurningBack,
        StrollPhase.TurningRight => StrollPhase.WalkingRight,
        StrollPhase.WalkingRight => StrollPhase.StandingRight,
        _ => StrollPhase.None
    };

    /// <summary>Whether a new stroll may begin right now.</summary>
    public bool CanStartStroll =>
        !IsPaused && !IsManualSleeping && !IsAutoSleeping && State != PetState.Dragging && !IsStrolling;

    /// <summary>
    /// Begins a stroll: turning to face the direction, walking, and coming back to face the viewer.
    /// A leftward stroll repeats its walk cycle <see cref="StrollRepeatCount"/> times; a rightward one
    /// is a single fixed pass. Returns false when a stroll cannot start (asleep, paused, being
    /// dragged, already strolling, or a zero repeat count on the left).
    /// </summary>
    public bool TryStartStroll(bool goLeft = true)
    {
        if (!CanStartStroll) return false;
        if (goLeft && StrollRepeatCount <= 0) return false;
        _sinceInteraction = TimeSpan.Zero;
        EnterPhase(goLeft ? StrollPhase.TurningLeft : StrollPhase.TurningRight);
        return true;
    }

    private void EnterPhase(StrollPhase phase)
    {
        Phase = phase;
        _stateElapsed = TimeSpan.Zero;
        State = phase switch
        {
            StrollPhase.Walking => PetState.WalkingLeft,
            StrollPhase.TurningLeft => PetState.TurningLeft,
            StrollPhase.Starting => PetState.WalkStarting,
            StrollPhase.Stopping => PetState.WalkStopping,
            StrollPhase.TurningBack => PetState.TurningBack,
            StrollPhase.WalkingRight => PetState.WalkingRight,
            StrollPhase.TurningRight => PetState.TurningRight,
            StrollPhase.StandingRight => PetState.StandingRight,
            _ => PetState.Idle
        };
    }

    private void EndStroll() => SetState(PetState.Idle);

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) return;

        _stateElapsed += elapsed;

        if (IsStrolling)
        {
            if (_stateElapsed >= PhaseDurationOf(Phase))
            {
                var next = NextPhase(Phase);
                if (next == StrollPhase.None) EndStroll();
                else EnterPhase(next);
            }
            // A stroll holds the pet awake and counts as interaction; it must not be interrupted by
            // the inactivity timer, and it suppresses free walking until it finishes.
            _sinceInteraction = TimeSpan.Zero;
            return;
        }

        var duration = DurationOf(State);
        if (duration > TimeSpan.Zero && _stateElapsed >= duration) SetState(PetState.Idle);

        if (IsManualSleeping || IsAutoSleeping || State == PetState.Dragging) return;
        _sinceInteraction += elapsed;
        if (_sinceInteraction >= AutoSleepAfter)
        {
            IsAutoSleeping = true;
            SetState(PetState.Sleeping);
        }
    }

    private void SetState(PetState state)
    {
        State = state;
        Phase = StrollPhase.None;
        _stateElapsed = TimeSpan.Zero;
    }

    public void Click()
    {
        _sinceInteraction = TimeSpan.Zero;
        if (IsManualSleeping || IsAutoSleeping)
        {
            IsManualSleeping = false;
            IsAutoSleeping = false;
            SetState(PetState.Idle);
            return;
        }
        // A click interrupts a stroll; the pet faces the viewer again from idle.
        SetState(PetState.Sighing);
    }

    public void ToggleManualSleep()
    {
        if (IsAutoSleeping)
        {
            IsAutoSleeping = false;
            IsManualSleeping = false;
            SetState(PetState.Idle);
            _sinceInteraction = TimeSpan.Zero;
            return;
        }
        IsManualSleeping = !IsManualSleeping;
        IsAutoSleeping = false;
        SetState(IsManualSleeping ? PetState.Sleeping : PetState.Idle);
        _sinceInteraction = TimeSpan.Zero;
    }

    public void Restore(bool paused, bool manualSleeping)
    {
        IsPaused = paused;
        IsManualSleeping = manualSleeping;
        IsAutoSleeping = false;
        SetState(manualSleeping ? PetState.Sleeping : PetState.Idle);
        _sinceInteraction = TimeSpan.Zero;
    }

    /// <summary>Abandons a stroll in progress, dropping straight back to idle.</summary>
    public void StopWalking()
    {
        if (IsStrolling || State is PetState.WalkingLeft or PetState.WalkingRight) SetState(PetState.Idle);
    }

    /// <summary>
    /// Kept for the rightward walk, which has no finished art yet. It never starts a stroll.
    /// </summary>
    public void SetWalking(bool right)
    {
        if (IsStrolling) return;
        if (!IsPaused && !IsManualSleeping && !IsAutoSleeping && State != PetState.Sighing)
            SetState(right ? PetState.WalkingRight : PetState.WalkingLeft);
    }

    public void BeginDrag()
    {
        _sinceInteraction = TimeSpan.Zero;
        SetState(PetState.Dragging);
    }

    public void EndDrag()
    {
        if (State == PetState.Dragging) SetState(PetState.Idle);
        _sinceInteraction = TimeSpan.Zero;
    }
}
