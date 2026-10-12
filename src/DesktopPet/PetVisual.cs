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
    private readonly VideoFrameCache _clips;
    private CancellationTokenSource? _externalCancellation;
    private IReadOnlyList<BitmapSource> _videoFrames = Array.Empty<BitmapSource>();
    private BitmapSource? _displayFrame;
    private Task<IReadOnlyList<BitmapSource>>? _stateLoad;
    private Task<IReadOnlyList<BitmapSource>>? _externalLoad;
    private PetState _videoFrameState;
    private bool _waitingForVideo;
    private bool _prefetchStarted;
    private bool _externalAnimationActive;
    private bool _externalAnimationReady;
    private double _externalPhase;
    private int _externalGeneration;
    private bool _externalCompletionQueued;
    private Action? _externalAnimationCompleted;
    public PetState State { get; set; } = PetState.Idle;
    public bool FacingRight { get; set; } = true;
    public bool IsUsingVideo => _videoFrames.Count > 0;
    public bool IsExternalAnimationActive => _externalAnimationActive;

    /// <summary>
    /// True after the current state's complete clip has been installed. Completed missing clips are
    /// also ready, allowing the state machine to recover rather than wait forever for absent art.
    /// </summary>
    public bool IsStateAnimationReady =>
        !_externalAnimationActive && _phaseState == State && !_waitingForVideo;

    internal BitmapSource? CurrentFrame => _displayFrame;
    internal double StateAnimationElapsed => _phase;

    public void BeginExternalAnimation(string fileName, Action? completed = null)
    {
        var generation = ++_externalGeneration;
        _externalAnimationActive = true;
        _externalAnimationReady = false;
        _externalPhase = 0;
        _externalCompletionQueued = false;
        _externalAnimationCompleted = completed;
        _videoFrames = Array.Empty<BitmapSource>();
        _waitingForVideo = true;
        _phaseState = null;
        _externalCancellation?.Cancel();
        _externalCancellation?.Dispose();
        _externalCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _externalCancellation.Token;
        _externalLoad = _clips.LoadFileAsync(fileName, token, firstFrame =>
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != _externalGeneration || !_externalAnimationActive ||
                    _externalAnimationReady || token.IsCancellationRequested) return;
                SetDisplayFrame(firstFrame);
            })));
        PrefetchStrollAnimations();
    }

    public PetVisual() : this(
        (state, token) => VideoFrameDecoder.LoadAsync(state, token),
        (fileName, token, firstFrame) => VideoFrameDecoder.LoadFileAsync(fileName, token, firstFrame)) { }

    internal PetVisual(
        Func<PetState, CancellationToken, Task<IReadOnlyList<BitmapSource>>> decodeState,
        Func<string, CancellationToken, Action<BitmapSource>?, Task<IReadOnlyList<BitmapSource>>> decodeFile)
    {
        _clips = new VideoFrameCache(decodeState, decodeFile, _lifetime.Token);
        IsHitTestVisible = true;
        SnapsToDevicePixels = true;
    }

    public void PrefetchStrollAnimations()
    {
        if (_prefetchStarted) return;
        _prefetchStarted = true;
        _ = _clips.GetOrLoad(PetState.Idle);
        foreach (var state in VideoClipCatalog.StrollStates) _ = _clips.GetOrLoad(state);
    }

    public void Advance(double seconds)
    {
        seconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        if (_externalAnimationActive)
        {
            var justBecameReady = false;
            if (_externalLoad is { IsCompleted: true } externalLoad)
            {
                _externalLoad = null;
                if (externalLoad.Status == TaskStatus.RanToCompletion && externalLoad.Result.Count > 0)
                {
                    _videoFrames = externalLoad.Result;
                    _externalAnimationReady = true;
                    _externalPhase = 0;
                    _waitingForVideo = false;
                    justBecameReady = true;
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
                if (!justBecameReady) _externalPhase += seconds;
                SetDisplayFrame(_videoFrames[
                    AnimationTiming.OneShotFrameIndex(_videoFrames.Count, _externalPhase, 30)]);
                var duration = _videoFrames.Count / 30.0;
                if (_externalPhase >= duration && !_externalCompletionQueued)
                {
                    _externalCompletionQueued = true;
                    var generation = _externalGeneration;
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.ApplicationIdle,
                        new Action(() =>
                        {
                            if (generation == _externalGeneration) CompleteExternalAnimation();
                        }));
                }
            }

            return;
        }

        var stateJustBecameReady = false;
        if (State != _phaseState)
        {
            _phaseState = State;
            _phase = 0;
            // Keep the preceding pose visible until the whole next clip is ready. Playback and the
            // controller can then start together, without freezing a lone first frame and jumping.
            _videoFrames = Array.Empty<BitmapSource>();
            _waitingForVideo = true;
            _stateLoad = _clips.GetOrLoad(State);
        }
        if (_stateLoad is { IsCompleted: true } load)
        {
            _videoFrames = load.Status == TaskStatus.RanToCompletion
                ? load.Result : Array.Empty<BitmapSource>();
            _ = load.Exception;
            _videoFrameState = State;
            _stateLoad = null;
            _waitingForVideo = false;
            _phase = 0;
            stateJustBecameReady = true;
        }
        if (_waitingForVideo) return;
        if (!stateJustBecameReady) _phase += seconds;
        SetDisplayFrame(_videoFrames.Count > 0
            ? _videoFrames[AnimationTiming.FrameIndex(_videoFrameState, _videoFrames.Count, _phase)]
            : null);
    }

    private void CompleteExternalAnimation()
    {
        if (!_externalAnimationActive) return;
        _externalAnimationActive = false;
        _externalAnimationReady = false;
        _externalCompletionQueued = false;
        _phaseState = null;
        _videoFrames = Array.Empty<BitmapSource>();
        _waitingForVideo = true;
        var completed = _externalAnimationCompleted;
        _externalAnimationCompleted = null;
        if (completed is not null)
            completed();
        else
            Advance(0);
    }

    private void SetDisplayFrame(BitmapSource? frame)
    {
        if (ReferenceEquals(_displayFrame, frame)) return;
        _displayFrame = frame;
        InvalidateVisual();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var point = hitTestParameters.HitPoint;
        return new PointHitTestResult(this, point);
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
        if (_displayFrame is not null)
            dc.DrawImage(_displayFrame, new Rect(0, 0, DesignWidth, DesignHeight));
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        if (Parent is null)
        {
            _lifetime.Cancel();
            _externalCancellation?.Cancel();
        }
        base.OnVisualParentChanged(oldParent);
    }

}
