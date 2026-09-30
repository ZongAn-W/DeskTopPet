namespace DesktopPet.Core;

public sealed class PetController
{
    private static readonly TimeSpan AutoSleepAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResponseDuration = TimeSpan.FromMilliseconds(800);
    private TimeSpan _sinceInteraction;
    private TimeSpan _sinceResponse;

    public PetState State { get; private set; } = PetState.Idle;
    public bool IsPaused { get; set; }
    public bool IsManualSleeping { get; private set; }
    public bool IsAutoSleeping { get; private set; }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) return;
        if (State == PetState.Responding)
        {
            _sinceResponse += elapsed;
            if (_sinceResponse >= ResponseDuration) State = PetState.Idle;
        }
        if (IsManualSleeping || IsAutoSleeping || State == PetState.Dragging) return;
        _sinceInteraction += elapsed;
        if (_sinceInteraction >= AutoSleepAfter)
        {
            IsAutoSleeping = true;
            State = PetState.Sleeping;
        }
    }

    public void Click()
    {
        _sinceInteraction = TimeSpan.Zero;
        if (IsManualSleeping || IsAutoSleeping)
        {
            IsManualSleeping = false;
            IsAutoSleeping = false;
            State = PetState.Idle;
            return;
        }
        _sinceResponse = TimeSpan.Zero;
        State = PetState.Responding;
    }

    public void ToggleManualSleep()
    {
        if (IsAutoSleeping)
        {
            IsAutoSleeping = false;
            IsManualSleeping = false;
            State = PetState.Idle;
            _sinceInteraction = TimeSpan.Zero;
            return;
        }
        IsManualSleeping = !IsManualSleeping;
        IsAutoSleeping = false;
        State = IsManualSleeping ? PetState.Sleeping : PetState.Idle;
        _sinceInteraction = TimeSpan.Zero;
    }

    public void Restore(bool paused, bool manualSleeping)
    {
        IsPaused = paused;
        IsManualSleeping = manualSleeping;
        IsAutoSleeping = false;
        State = manualSleeping ? PetState.Sleeping : PetState.Idle;
        _sinceInteraction = TimeSpan.Zero;
    }

    public void StopWalking()
    {
        if (State is PetState.WalkingLeft or PetState.WalkingRight) State = PetState.Idle;
    }

    public void SetWalking(bool right)
    {
        if (!IsPaused && !IsManualSleeping && !IsAutoSleeping && State != PetState.Responding)
            State = right ? PetState.WalkingRight : PetState.WalkingLeft;
    }

    public void BeginDrag()
    {
        _sinceInteraction = TimeSpan.Zero;
        State = PetState.Dragging;
    }

    public void EndDrag()
    {
        if (State == PetState.Dragging) State = PetState.Idle;
        _sinceInteraction = TimeSpan.Zero;
    }
}
