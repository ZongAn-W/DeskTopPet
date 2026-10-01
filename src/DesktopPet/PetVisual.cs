using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.Core;
using Point = System.Windows.Point;

namespace DesktopPet;

public sealed class PetVisual : FrameworkElement
{
    // Design canvas the sprite frames are authored on; also the vector placeholder's coordinate space.
    public const double DesignWidth = 180;
    public const double DesignHeight = 210;

    // The clickable face region as fractions of the canvas. Derived from the original
    // hard-coded 38,18,104,145 rectangle on the 180 x 210 canvas.
    private const double FaceLeftFraction = 38.0 / DesignWidth;
    private const double FaceTopFraction = 18.0 / DesignHeight;
    private const double FaceWidthFraction = 104.0 / DesignWidth;
    private const double FaceHeightFraction = 145.0 / DesignHeight;

    private double _phase;
    private PetState? _phaseState;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _videoCancellation;
    private IReadOnlyList<BitmapSource> _videoFrames = Array.Empty<BitmapSource>();
    private BitmapSource? _displayFrame;
    private Task<IReadOnlyList<BitmapSource>>? _videoLoad;
    private PetState _videoFrameState;
    private bool _waitingForVideo;
    private bool _externalAnimationActive;
    private bool _externalAnimationReady;
    private double _externalPhase;
    private bool _externalCompletionQueued;
    private Action? _externalAnimationCompleted;
    public PetState State { get; set; } = PetState.Idle;
    public bool FacingRight { get; set; } = true;
    public bool IsUsingVideo => _videoFrames.Count > 0;

    public void BeginExternalAnimation(string fileName, Action? completed = null)
    {
        _externalAnimationActive = true;
        _externalAnimationReady = false;
        _externalPhase = 0;
        _externalCompletionQueued = false;
        _externalAnimationCompleted = completed;
        _videoFrames = Array.Empty<BitmapSource>();
        _waitingForVideo = true;
        _videoCancellation?.Cancel();
        _videoCancellation?.Dispose();
        _videoCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _videoCancellation.Token;
        _videoLoad = VideoFrameDecoder.LoadFileAsync(fileName, token, firstFrame =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_externalAnimationActive || _externalAnimationReady || token.IsCancellationRequested) return;
                _videoFrames = [firstFrame];
                _waitingForVideo = false;
                InvalidateVisual();
            })));
        _ = _videoLoad.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(InvalidateVisual)),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public PetVisual()
    {
        IsHitTestVisible = true;
        SnapsToDevicePixels = true;
    }

    public void Advance(double seconds)
    {
        if (_externalAnimationActive)
        {
            if (_videoLoad is { IsCompleted: true } externalLoad)
            {
                _videoLoad = null;
                if (externalLoad.Status == TaskStatus.RanToCompletion && externalLoad.Result.Count > 0)
                {
                    _videoFrames = externalLoad.Result;
                    _externalAnimationReady = true;
                    _externalPhase = 0;
                    _waitingForVideo = false;
                }
                else
                {
                    _ = externalLoad.Exception;
                    CompleteExternalAnimation();
                    return;
                }
            }

            if (_externalAnimationReady)
            {
                _externalPhase += Math.Max(0, seconds);
                var duration = _videoFrames.Count / 30.0;
                if (_externalPhase >= duration && !_externalCompletionQueued)
                {
                    _externalCompletionQueued = true;
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.ApplicationIdle,
                        new Action(CompleteExternalAnimation));
                }
            }

            InvalidateVisual();
            return;
        }

        // The animation clock is per state, not global. One ever-growing shared counter made a
        // one-shot sequence compute a frame index far past its end, clamp to its final frame, and
        // sit there for the whole phase — the turns and the walk-up/walk-down showed a single still.
        // Only the looping walk appeared to work, because wrapping hid the fault.
        if (State != _phaseState)
        {
            _phaseState = State;
            // Hold the previous clip's last rendered frame while the next clip decodes. The left
            // walk clips intentionally hand off through matching poses, so this avoids a transparent
            // gap without showing a partially decoded frame.
            _videoFrames = Array.Empty<BitmapSource>();
            _waitingForVideo = true;
            BeginVideoLoad(State);
        }
        if (_videoLoad is { IsCompletedSuccessfully: true } load && load.Result.Count > 0)
        {
            _videoFrames = load.Result;
            _videoFrameState = State;
            _videoLoad = null;
            _phase = 0;
            _waitingForVideo = false;
            var completed = _externalAnimationCompleted;
            _externalAnimationCompleted = null;
            if (completed is not null)
                Dispatcher.BeginInvoke(completed);
            // Let the first frame render for a complete tick before consuming elapsed time. This
            // makes the handoff deterministic even when decoding finishes between render callbacks.
            InvalidateVisual();
            return;
        }
        else if (_videoLoad is { IsCompleted: true })
        {
            _videoLoad = null;
            _videoFrames = Array.Empty<BitmapSource>();
            _displayFrame = null;
            _waitingForVideo = false;
        }
        if (!_waitingForVideo)
            _phase += seconds;
        InvalidateVisual();
    }

    private void CompleteExternalAnimation()
    {
        if (!_externalAnimationActive) return;
        _externalAnimationActive = false;
        _externalAnimationReady = false;
        _externalCompletionQueued = false;
        var completed = _externalAnimationCompleted;
        _externalAnimationCompleted = null;
        if (completed is not null)
            completed();
        else
        {
            _phaseState = State;
            _phase = 0;
            _waitingForVideo = true;
            BeginVideoLoad(State);
        }
        InvalidateVisual();
    }

    private void BeginVideoLoad(PetState state)
    {
        _videoCancellation?.Cancel();
        _videoCancellation?.Dispose();
        _videoCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _videoLoad = VideoFrameDecoder.LoadAsync(state, _videoCancellation.Token);
        var load = _videoLoad;
        _ = load.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(InvalidateVisual)),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var point = hitTestParameters.HitPoint;
        return FaceRect().Contains(point) ? new PointHitTestResult(this, point) : null;
    }

    /// <summary>
    /// The clickable face region, expressed as fractions of the current render size so it
    /// stays correct if the window is ever resized. Falls back to the design canvas
    /// (180 x 210) before the first layout pass, when ActualWidth/Height are still zero.
    /// </summary>
    public Rect FaceRect()
    {
        var width = ActualWidth > 0 ? ActualWidth : DesignWidth;
        var height = ActualHeight > 0 ? ActualHeight : DesignHeight;
        return new Rect(
            width * FaceLeftFraction,
            height * FaceTopFraction,
            width * FaceWidthFraction,
            height * FaceHeightFraction);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_waitingForVideo)
        {
            if (_displayFrame is not null)
                dc.DrawImage(_displayFrame, new Rect(0, 0, DesignWidth, DesignHeight));
            return;
        }
        if (_videoFrames.Count > 0)
        {
            var frameIndex = _externalAnimationActive
                ? _externalAnimationReady
                    ? AnimationTiming.OneShotFrameIndex(_videoFrames.Count, _externalPhase, 30)
                    : 0
                    : AnimationTiming.FrameIndex(_videoFrameState, _videoFrames.Count, _phase);
            _displayFrame = _videoFrames[frameIndex];
            dc.DrawImage(_displayFrame,
                new Rect(0, 0, DesignWidth, DesignHeight));
            return;
        }
        // Video is the only animation source. A missing clip leaves the transparent canvas empty.
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        if (Parent is null)
        {
            _lifetime.Cancel();
            _videoCancellation?.Cancel();
        }
        base.OnVisualParentChanged(oldParent);
    }

}
