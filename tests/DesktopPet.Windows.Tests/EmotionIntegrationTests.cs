using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet.Windows.Tests;

public sealed class EmotionIntegrationTests
{
    [Fact]
    public async Task SuccessfulReplyUpdatesBothViewsAndPersistsAcrossNewChatAndRestart()
    {
        await RunOnSta(async () =>
        {
            using var files = new TestFiles();
            using (var runtime = files.CreateRuntime((request, _) => Task.FromResult(IsSummary(request)
                ? Ok("# Desktop Pet Memory\n") : EmotionReply("I'm happy to help.", "Happy", 35))))
            {
                var full = new ChatWindow(runtime);
                var bubble = new BubbleChatWindow(runtime);
                try
                {
                    await runtime.SendAsync("Thank you", default);
                    Assert.Equal(EmotionKind.Happy, runtime.Emotion.Kind);
                    Assert.Equal("开心", ((TextBlock)full.FindName("EmotionLabel")).Text);
                    Assert.Equal("开心", ((TextBlock)bubble.FindName("EmotionLabel")).Text);
                    Assert.Equal("I'm happy to help.", runtime.Entries[^1].Text);
                    runtime.Clear();
                    await runtime.EndConversationAsync();
                    Assert.Equal(EmotionKind.Happy, runtime.Emotion.Kind);
                    Assert.Single(runtime.Entries);
                    Assert.Equal(EmotionKind.Happy, files.Emotions.Load(DateTimeOffset.UtcNow).Kind);
                }
                finally { full.CloseForExit(); bubble.CloseForExit(); }
            }
            using var restarted = files.CreateRuntime((_, _) => Task.FromResult(EmotionReply("Still cheerful.", "Happy", 40)));
            Assert.Equal(EmotionKind.Happy, restarted.Emotion.Kind);
            var saved = files.Emotions.Load(DateTimeOffset.UtcNow);
            Assert.Equal(saved.Intensity, restarted.Emotion.Intensity);
        });
    }

    [Fact]
    public async Task CancellationDoesNotChangeOrSaveEmotion()
    {
        using var files = new TestFiles();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime = files.CreateRuntime(async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return EmotionReply("Never committed", "Angry", 40);
        });
        var pending = runtime.SendAsync("message", default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(EmotionKind.Calm, runtime.Emotion.Kind);
        Assert.False(File.Exists(files.Emotions.Path));
        Assert.Single(runtime.Entries);
    }

    [Fact]
    public async Task InvalidResponseKeepsPreviousSavedMoodAndRestoresTheTurn()
    {
        using var files = new TestFiles();
        files.Emotions.Save(new EmotionState { Kind = EmotionKind.Concerned, Intensity = 35 });
        using var runtime = files.CreateRuntime((_, _) => Task.FromResult(Ok("{invalid")));
        await Assert.ThrowsAsync<ChatServiceException>(() => runtime.SendAsync("message", default));
        Assert.Equal(EmotionKind.Concerned, runtime.Emotion.Kind);
        Assert.Equal(EmotionKind.Concerned, files.Emotions.Load(DateTimeOffset.UtcNow).Kind);
        Assert.Single(runtime.Entries);
    }

    private static bool IsSummary(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        return body.RootElement.GetProperty("max_tokens").GetInt32() == 4096;
    }

    private static HttpResponseMessage EmotionReply(string reply, string emotion, int intensity) =>
        Ok(JsonSerializer.Serialize(new { reply, emotion, intensity }));

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
            Encoding.UTF8, "application/json")
    };

    private static Task RunOnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private sealed class TestFiles : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"pet-emotion-ui-{Guid.NewGuid():N}.bin");
        public EmotionStore Emotions { get; } = new(Path.Combine(Path.GetTempPath(), $"pet-emotion-ui-{Guid.NewGuid():N}.json"));

        public ChatRuntime CreateRuntime(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            var settings = new ChatSettingsStore(_path);
            settings.Save(new ChatSettings { ApiKey = "emotion-ui-test-key" });
            return new ChatRuntime(new HttpClient(new Handler(respond)), settings, emotionStore: Emotions);
        }

        public void Dispose()
        {
            File.Delete(_path);
            File.Delete(_path + ".memory.md");
            File.Delete(_path + ".memory.md.pending.json");
            File.Delete(Emotions.Path);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
}
