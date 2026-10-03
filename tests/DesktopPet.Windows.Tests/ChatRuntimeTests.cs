using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using DesktopPet.Core;

namespace DesktopPet.Windows.Tests;

public sealed class ChatRuntimeTests
{
    [Fact]
    public void ConstructorLoadsSettingsAndAddsOneSharedGreeting()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings
        {
            ApiKey = "runtime-test-key",
            BubbleMessageCount = 99
        });

        try
        {
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("reply")));

            Assert.Equal("runtime-test-key", runtime.Settings.ApiKey);
            Assert.Equal(5, runtime.Settings.BubbleMessageCount);
            Assert.Single(runtime.Entries);
            Assert.Equal("assistant", runtime.Entries[0].Role);
            Assert.Empty(runtime.Session.Messages);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SuccessfulSendUpdatesSharedEntriesAndSingleSession()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings { ApiKey = "runtime-test-key" });
        try
        {
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("reply")));
            var states = 0;
            runtime.StateChanged += (_, _) => states++;

            await runtime.SendAsync("hello", default);

            Assert.Equal(3, runtime.Entries.Count);
            Assert.Equal("hello", runtime.Entries[1].Text);
            Assert.Equal("reply", runtime.Entries[2].Text);
            Assert.Equal("user", runtime.Entries[1].Role);
            Assert.Equal("assistant", runtime.Entries[2].Role);
            Assert.Equal(2, runtime.Session.Messages.Count);
            Assert.Equal("hello", runtime.Session.Messages[0].Content);
            Assert.Equal("reply", runtime.Session.Messages[1].Content);
            Assert.False(runtime.IsBusy);
            Assert.True(states >= 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task CancellationRollsBackVisibleUserEntryAndLeavesSessionUntouched()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings { ApiKey = "runtime-test-key" });
        try
        {
            using var runtime = CreateRuntime(store, async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Ok("never");
            });
            var states = 0;
            runtime.StateChanged += (_, _) => states++;

            var request = runtime.SendAsync("draft", default);
            Assert.True(runtime.IsBusy);
            runtime.Cancel();
            runtime.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.Single(runtime.Entries);
            Assert.Empty(runtime.Session.Messages);
            Assert.False(runtime.IsBusy);
            Assert.True(states >= 2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task FailedRequestRollsBackVisibleUserEntryAndLeavesSessionUntouched()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings { ApiKey = "runtime-test-key" });
        try
        {
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.PaymentRequired)));

            await Assert.ThrowsAsync<ChatServiceException>(() => runtime.SendAsync("retry me", default));

            Assert.Single(runtime.Entries);
            Assert.Empty(runtime.Session.Messages);
            Assert.False(runtime.IsBusy);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClearResetsSharedEntriesAndSessionToOneGreeting()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings { ApiKey = "runtime-test-key" });
        try
        {
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("reply")));
            await runtime.SendAsync("hello", default);

            runtime.Clear();

            Assert.Single(runtime.Entries);
            Assert.Empty(runtime.Session.Messages);
            Assert.False(runtime.IsBusy);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClearCancelsActiveRequestButKeepsItBusyUntilItHasExited()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings { ApiKey = "runtime-test-key" });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var runtime = CreateRuntime(store, async (_, _) =>
            {
                entered.SetResult();
                return await releaseResponse.Task;
            });

            var activeRequest = runtime.SendAsync("old conversation", default);
            await entered.Task;

            runtime.Clear();

            Assert.True(runtime.IsBusy);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SendAsync("new conversation", default));
            releaseResponse.SetResult(Ok("late reply"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activeRequest);

            Assert.False(runtime.IsBusy);
            Assert.Single(runtime.Entries);
            Assert.Empty(runtime.Session.Messages);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SaveSettingsNormalizesPersistsAndNotifies()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        try
        {
            using var runtime = CreateRuntime(store, (_, _) => Task.FromResult(Ok("reply")));
            var states = 0;
            runtime.StateChanged += (_, _) => states++;

            runtime.SaveSettings(new ChatSettings
            {
                ApiKey = "updated-key",
                BubbleMessageCount = 1,
                DefaultPresentation = (ChatPresentationMode)999,
                BubbleDismiss = (BubbleDismissMode)999
            });

            Assert.Equal("updated-key", runtime.Settings.ApiKey);
            Assert.Equal(3, runtime.Settings.BubbleMessageCount);
            Assert.Equal(ChatPresentationMode.Bubble, runtime.Settings.DefaultPresentation);
            Assert.Equal(BubbleDismissMode.ClickOutside, runtime.Settings.BubbleDismiss);
            Assert.Equal(runtime.Settings, store.Load());
            Assert.Equal(1, states);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OnlyOneRequestMayBeActive()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings { ApiKey = "runtime-test-key" });
        try
        {
            using var runtime = CreateRuntime(store, async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return Ok("never");
            });
            var first = runtime.SendAsync("first", default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SendAsync("second", default));
            runtime.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClearDuringRequestPreventsLateCommitAndBlocksNewRequestUntilCompletion()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings { ApiKey = "runtime-test-key" });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var runtime = CreateRuntime(store, async (_, _) =>
            {
                started.TrySetResult();
                return await release.Task;
            });
            var first = runtime.SendAsync("old request", default);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            runtime.Clear();

            Assert.True(runtime.IsBusy);
            await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.SendAsync("new request", default));
            release.SetResult(Ok("late reply"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            Assert.False(runtime.IsBusy);
            Assert.Single(runtime.Entries);
            Assert.Empty(runtime.Session.Messages);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DisposeDuringRequestPreventsLateSessionCommit()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        store.Save(new ChatSettings { ApiKey = "runtime-test-key" });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = CreateRuntime(store, async (_, _) =>
        {
            started.TrySetResult();
            return await release.Task;
        });
        try
        {
            var request = runtime.SendAsync("late request", default);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            runtime.Dispose();
            release.SetResult(Ok("late reply"));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            Assert.Empty(runtime.Session.Messages);
        }
        finally
        {
            runtime.Dispose();
            File.Delete(path);
        }
    }

    private static ChatRuntime CreateRuntime(
        ChatSettingsStore store,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        return new ChatRuntime(new HttpClient(new ResponseHandler(respond)), store);
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{\"choices\":[{{\"message\":{{\"content\":\"{text}\"}}}}]}}", Encoding.UTF8, "application/json")
    };

    private static string NewSettingsPath() => Path.Combine(Path.GetTempPath(), $"desktop-pet-runtime-test-{Guid.NewGuid():N}.bin");

    private sealed class ResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
