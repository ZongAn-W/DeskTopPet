using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet.Windows.Tests;

public sealed class BubbleChatWindowTests
{
    [Fact]
    public void ShowsOnlyTheConfiguredLastEntriesFromTheSharedRuntime()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new DesktopPet.Core.ChatSettings
            {
                ApiKey = "bubble-test-key",
                BubbleMessageCount = 3
            });
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("reply")));
            runtime.Entries.Add(new ChatEntry("你", "one", "#EEE5F5") { Role = "user" });
            runtime.Entries.Add(new ChatEntry("她", "two", "#FFFFFF") { Role = "assistant" });
            runtime.Entries.Add(new ChatEntry("你", "three", "#EEE5F5") { Role = "user" });
            runtime.Entries.Add(new ChatEntry("她", "four", "#FFFFFF") { Role = "assistant" });
            var window = new BubbleChatWindow(runtime);
            try
            {
                window.Show();
                var list = (ItemsControl)window.FindName("MessagesList");
                Assert.Equal(new[] { "two", "three", "four" }, list.Items.Cast<ChatEntry>().Select(entry => entry.Text));
            }
            finally
            {
                window.CloseForExit();
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void ExpandRaisesEventAndNewChatClearsTheSharedRuntime()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new DesktopPet.Core.ChatSettings { ApiKey = "bubble-test-key" });
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("reply")));
            runtime.Entries.Add(new ChatEntry("你", "existing", "#EEE5F5") { Role = "user" });
            var window = new BubbleChatWindow(runtime);
            var expanded = 0;
            window.ExpandRequested += (_, _) => expanded++;
            try
            {
                window.Show();
                ((Button)window.FindName("ExpandButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, expanded);
                ((Button)window.FindName("NewChatButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(runtime.Entries);
                Assert.Empty(runtime.Session.Messages);
            }
            finally
            {
                window.CloseForExit();
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void CancelRestoresDraftInTheBubble()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new DesktopPet.Core.ChatSettings { ApiKey = "bubble-test-key" });
            using var http = new HttpClient(new ResponseHandler(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Ok("never");
            }));
            using var runtime = new ChatRuntime(http, store);
            var window = new BubbleChatWindow(runtime);
            try
            {
                window.Show();
                var input = (TextBox)window.FindName("InputBox");
                var send = (Button)window.FindName("SendButton");
                input.Text = "keep this draft";
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(input.IsReadOnly);
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => !input.IsReadOnly);
                Assert.Equal("keep this draft", input.Text);
                Assert.Contains("取消", ((TextBlock)window.FindName("StatusText")).Text);
            }
            finally
            {
                window.CloseForExit();
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void EnterSendsTheCurrentDraft()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new DesktopPet.Core.ChatSettings { ApiKey = "bubble-test-key" });
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("sent by enter")));
            var window = new BubbleChatWindow(runtime);
            try
            {
                window.Show();
                var input = (TextBox)window.FindName("InputBox");
                input.Text = "enter this";
                var source = PresentationSource.FromVisual(window)!;
                var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
                {
                    RoutedEvent = UIElement.PreviewKeyDownEvent
                };
                input.RaiseEvent(keyEvent);
                PumpUntil(() => !runtime.IsBusy && runtime.Session.Messages.Count == 2);
                Assert.Equal("enter this", runtime.Session.Messages[0].Content);
                Assert.Equal("sent by enter", runtime.Session.Messages[1].Content);
                Assert.True(keyEvent.Handled);
            }
            finally
            {
                window.CloseForExit();
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void IdleDismissTimerResetsOnTypingAndStopsWhileRequestIsActive()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new DesktopPet.Core.ChatSettings
            {
                ApiKey = "bubble-test-key",
                BubbleDismiss = BubbleDismissMode.ClickOutsideOrIdle
            });
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var runtime = CreateRuntime(store, async (_, _) =>
            {
                started.TrySetResult();
                return await release.Task;
            });
            var timers = new FakeTimerFactory();
            var window = new BubbleChatWindow(runtime, timers.Create);
            try
            {
                window.Show();
                var input = (TextBox)window.FindName("InputBox");
                input.Text = "typing";
                var firstTimer = Assert.Single(timers.Timers.Where(timer => timer.IsRunning));
                input.Text = "still typing";
                var currentTimer = Assert.Single(timers.Timers.Where(timer => timer.IsRunning));
                Assert.NotSame(firstTimer, currentTimer);
                Assert.False(firstTimer.IsRunning);
                Assert.Equal(TimeSpan.FromSeconds(15), currentTimer.Interval);

                ((Button)window.FindName("SendButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => started.Task.IsCompleted);
                Assert.Empty(timers.Timers.Where(timer => timer.IsRunning));
                release.TrySetResult(Ok("done"));
                PumpUntil(() => !runtime.IsBusy);
                Assert.Single(timers.Timers.Where(timer => timer.IsRunning));
            }
            finally
            {
                release.TrySetCanceled();
                window.CloseForExit();
                File.Delete(path);
            }
        });
    }

    [Theory]
    [InlineData(BubbleDismissMode.ClickOutsideOrIdle, 15)]
    [InlineData(BubbleDismissMode.AfterReply, 20)]
    public void SuccessfulReplyStartsTheConfiguredDismissTimer(BubbleDismissMode mode, int seconds)
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new DesktopPet.Core.ChatSettings { ApiKey = "bubble-test-key", BubbleDismiss = mode });
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("reply")));
            var timers = new FakeTimerFactory();
            var window = new BubbleChatWindow(runtime, timers.Create);
            try
            {
                window.Show();
                var send = (Button)window.FindName("SendButton");
                var input = (TextBox)window.FindName("InputBox");
                input.Text = "hello";
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => !runtime.IsBusy);
                var timer = Assert.Single(timers.Timers.Where(candidate => candidate.IsRunning));
                Assert.Equal(TimeSpan.FromSeconds(seconds), timer.Interval);
                timer.Tick();
                Assert.False(window.IsVisible);
            }
            finally
            {
                window.CloseForExit();
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void ClickOutsideDismissesWhenInputIsNotFocused()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new DesktopPet.Core.ChatSettings { ApiKey = "bubble-test-key" });
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("reply")));
            var window = new BubbleChatWindow(runtime);
            try
            {
                window.Show();
                Keyboard.ClearFocus();
                InvokePrivate(window, "HandleDeactivated");
                Assert.False(window.IsVisible);
            }
            finally
            {
                window.CloseForExit();
                File.Delete(path);
            }
        });
    }

    private static ChatRuntime CreateRuntime(ChatSettingsStore store, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(new HttpClient(new ResponseHandler(respond)), store);

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{\"choices\":[{{\"message\":{{\"content\":\"{text}\"}}}}]}}", Encoding.UTF8, "application/json")
    };

    private static string NewSettingsPath() => Path.Combine(Path.GetTempPath(), $"desktop-pet-bubble-test-{Guid.NewGuid():N}.bin");

    private static void InvokePrivate(object instance, string name) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, null);

    private static void RunOnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); }
            catch (Exception failure) { error = failure; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("UI integration test timed out");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!completed())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Chat did not complete");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }

    private sealed class FakeTimerFactory
    {
        public List<FakeTimer> Timers { get; } = [];
        public IBubbleDismissTimer Create(TimeSpan interval, EventHandler tick)
        {
            var timer = new FakeTimer(interval, tick);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class FakeTimer(TimeSpan interval, EventHandler tick) : IBubbleDismissTimer
    {
        private readonly EventHandler _tick = tick;
        public TimeSpan Interval { get; } = interval;
        public bool IsRunning { get; private set; }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
        public void Tick() => _tick(this, EventArgs.Empty);
        public void Dispose() => Stop();
    }
}
