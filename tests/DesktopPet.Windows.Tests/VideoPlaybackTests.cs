using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet.Windows.Tests;

public sealed class VideoPlaybackTests
{
    [Fact]
    public void A_cold_state_starts_at_frame_zero_only_after_the_complete_clip_is_ready()
    {
        RunOnSta(() =>
        {
            var pending = NewLoad();
            var frames = Frames(4);
            var visual = CreateVisual((_, _) => pending.Task);
            visual.State = PetState.WalkingLeft;

            visual.Advance(10);
            Assert.False(visual.IsStateAnimationReady);
            Assert.Equal(0, visual.StateAnimationElapsed);
            Assert.Null(visual.CurrentFrame);

            pending.SetResult(frames);
            WaitUntil(() =>
            {
                visual.Advance(10);
                return visual.IsStateAnimationReady;
            });

            Assert.Equal(0, visual.StateAnimationElapsed);
            Assert.Same(frames[0], visual.CurrentFrame);
            visual.Advance(1 / 30.0 + 0.001);
            Assert.Same(frames[1], visual.CurrentFrame);
        });
    }

    [Fact]
    public void Late_state_loads_cannot_replace_the_current_clip_and_cached_reentry_restarts_it()
    {
        RunOnSta(() =>
        {
            var leftLoad = NewLoad();
            var rightLoad = NewLoad();
            var left = Frames(3);
            var right = Frames(3);
            var calls = new ConcurrentDictionary<PetState, int>();
            var visual = CreateVisual((state, _) =>
            {
                calls.AddOrUpdate(state, 1, (_, count) => count + 1);
                return state == PetState.WalkingLeft ? leftLoad.Task : rightLoad.Task;
            });

            visual.State = PetState.WalkingLeft;
            visual.Advance(0);
            visual.State = PetState.TurningRight;
            visual.Advance(0);
            WaitUntil(() => calls.Count == 2);

            leftLoad.SetResult(left);
            visual.Advance(1);
            Assert.False(visual.IsStateAnimationReady);
            Assert.Null(visual.CurrentFrame);

            rightLoad.SetResult(right);
            WaitUntil(() =>
            {
                visual.Advance(0);
                return visual.IsStateAnimationReady;
            });
            Assert.Same(right[0], visual.CurrentFrame);
            visual.Advance(0.04);
            Assert.Same(right[1], visual.CurrentFrame);

            visual.State = PetState.WalkingLeft;
            WaitUntil(() =>
            {
                visual.Advance(0);
                return visual.IsStateAnimationReady;
            });
            Assert.Same(left[0], visual.CurrentFrame);
            Assert.Equal(0, visual.StateAnimationElapsed);

            visual.State = PetState.TurningRight;
            visual.Advance(10);
            Assert.True(visual.IsStateAnimationReady);
            Assert.Same(right[0], visual.CurrentFrame);
            Assert.All(calls.Values, count => Assert.Equal(1, count));
        });
    }

    [Fact]
    public void An_external_first_frame_does_not_consume_the_clip_while_decoding_continues()
    {
        RunOnSta(() =>
        {
            var pending = NewLoad();
            var frames = Frames(3);
            var completed = false;
            var prefetched = new ConcurrentDictionary<PetState, int>();
            var visual = new PetVisual(
                (state, _) =>
                {
                    prefetched.AddOrUpdate(state, 1, (_, count) => count + 1);
                    return Task.FromResult(Frames(1));
                },
                (_, _, firstFrame) =>
                {
                    firstFrame?.Invoke(frames[0]);
                    return pending.Task;
                });

            visual.BeginExternalAnimation("appear.mov", () => completed = true);
            DrainDispatcher();
            visual.Advance(20);
            Assert.True(visual.IsExternalAnimationActive);
            Assert.False(visual.IsStateAnimationReady);
            Assert.Same(frames[0], visual.CurrentFrame);
            Assert.False(completed);
            WaitUntil(() => VideoClipCatalog.StrollStates.All(prefetched.ContainsKey));

            pending.SetResult(frames);
            WaitUntil(() =>
            {
                visual.Advance(20);
                return visual.IsUsingVideo;
            });
            Assert.Same(frames[0], visual.CurrentFrame);
            Assert.False(completed);
            visual.Advance(0.04);
            Assert.Same(frames[1], visual.CurrentFrame);
            visual.Advance(1);
            DrainDispatcher();
            Assert.True(completed);
            Assert.False(visual.IsExternalAnimationActive);
            Assert.All(prefetched.Values, count => Assert.Equal(1, count));
        });
    }

    [Fact]
    public void Replacing_an_external_animation_ignores_old_callbacks_and_queued_completion()
    {
        RunOnSta(() =>
        {
            var loads = new ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<BitmapSource>>>();
            var callbacks = new ConcurrentDictionary<string, Action<BitmapSource>?>();
            var oldFrames = Frames(2);
            var newFrames = Frames(2);
            var oldCompleted = false;
            var newCompleted = false;
            var visual = new PetVisual(
                (_, _) => Task.FromResult(Frames(1)),
                (file, _, firstFrame) =>
                {
                    callbacks[file] = firstFrame;
                    loads[file] = NewLoad();
                    return loads[file].Task;
                });

            visual.BeginExternalAnimation("old.mov", () => oldCompleted = true);
            loads["old.mov"].SetResult(oldFrames);
            WaitUntil(() =>
            {
                visual.Advance(0);
                return visual.IsUsingVideo;
            });
            visual.Advance(1); // Queue completion, then replace it before the dispatcher runs it.
            visual.BeginExternalAnimation("new.mov", () => newCompleted = true);
            WaitUntil(() => callbacks.ContainsKey("new.mov"));
            callbacks["new.mov"]?.Invoke(newFrames[0]);
            callbacks["old.mov"]?.Invoke(oldFrames[0]);
            DrainDispatcher();

            Assert.False(oldCompleted);
            Assert.True(visual.IsExternalAnimationActive);
            Assert.Same(newFrames[0], visual.CurrentFrame);

            loads["new.mov"].SetResult(newFrames);
            WaitUntil(() =>
            {
                visual.Advance(0);
                return visual.IsUsingVideo;
            });
            visual.Advance(1);
            DrainDispatcher();
            Assert.True(newCompleted);
        });
    }

    [Fact]
    public async Task Foreground_and_prefetched_clips_share_two_decode_slots_and_reuse_complete_frames()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = Frames(2);
        var active = 0;
        var maxActive = 0;
        var calls = 0;
        async Task<IReadOnlyList<BitmapSource>> Decode(CancellationToken token)
        {
            var running = Interlocked.Increment(ref active);
            Interlocked.Increment(ref calls);
            int observed;
            do { observed = maxActive; }
            while (running > observed && Interlocked.CompareExchange(ref maxActive, running, observed) != observed);
            if (running == 2) twoStarted.TrySetResult();
            try { await release.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref active); }
            return frames;
        }

        var cache = new VideoFrameCache((_, token) => Decode(token), (_, token, _) => Decode(token), default);
        var external = cache.LoadFileAsync("appear.mov", default);
        var states = VideoClipCatalog.StrollStates.Select(cache.GetOrLoad).ToArray();
        Assert.Same(states[0], cache.GetOrLoad(VideoClipCatalog.StrollStates[0]));
        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, Volatile.Read(ref calls));
        release.SetResult();
        await Task.WhenAll(states.Append(external)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, maxActive);
        Assert.Equal(7, calls);
        foreach (var state in VideoClipCatalog.StrollStates)
        {
            var cached = await cache.GetOrLoad(state);
            Assert.Same(frames[0], cached[0]);
            Assert.All(cached, frame => Assert.True(frame.IsFrozen));
        }
        Assert.Equal(7, calls);
    }

    [Fact]
    public async Task Failed_loads_retry_after_a_cooldown_instead_of_becoming_permanent_or_spinning()
    {
        var now = DateTimeOffset.UtcNow;
        var calls = 0;
        var frames = Frames(2);
        var cache = new VideoFrameCache(
            (_, _) => Task.FromResult<IReadOnlyList<BitmapSource>>(
                ++calls == 1 ? Array.Empty<BitmapSource>() : frames),
            (_, _, _) => Task.FromResult<IReadOnlyList<BitmapSource>>(Array.Empty<BitmapSource>()),
            default, () => now);

        var failed = cache.GetOrLoad(PetState.WalkingRight);
        Assert.Empty(await failed);
        Assert.Same(failed, cache.GetOrLoad(PetState.WalkingRight));
        Assert.Equal(1, calls);

        now = now.AddSeconds(6);
        var retry = cache.GetOrLoad(PetState.WalkingRight);
        Assert.NotSame(failed, retry);
        Assert.Equal(2, (await retry).Count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Lifetime_cancellation_stops_active_and_queued_prefetches()
    {
        using var lifetime = new CancellationTokenSource();
        var twoStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new VideoFrameCache(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2) twoStarted.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Array.Empty<BitmapSource>();
        }, (_, _, _) => Task.FromResult<IReadOnlyList<BitmapSource>>(Array.Empty<BitmapSource>()), lifetime.Token);

        var loads = VideoClipCatalog.StrollStates.Select(cache.GetOrLoad).ToArray();
        await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Cancel();
        var results = await Task.WhenAll(loads).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, frames => Assert.Empty(frames));
        Assert.Equal(2, calls);
    }

    private static PetVisual CreateVisual(Func<PetState, CancellationToken, Task<IReadOnlyList<BitmapSource>>> decode) =>
        new(decode, (_, _, _) => Task.FromResult<IReadOnlyList<BitmapSource>>(Array.Empty<BitmapSource>()));

    private static TaskCompletionSource<IReadOnlyList<BitmapSource>> NewLoad() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static IReadOnlyList<BitmapSource> Frames(int count) => Enumerable.Range(0, count).Select(index =>
    {
        var frame = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { (byte)index, 0, 0, 255 }, 4);
        frame.Freeze();
        return frame;
    }).ToArray();

    private static void WaitUntil(Func<bool> condition) =>
        Assert.True(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(5)), "The controlled decoder did not complete.");

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.SystemIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The playback test did not finish.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
