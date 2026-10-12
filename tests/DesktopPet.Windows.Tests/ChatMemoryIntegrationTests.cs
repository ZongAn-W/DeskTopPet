using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet.Windows.Tests;

public sealed class ChatMemoryIntegrationTests
{
    private const string MemoryDocument = "# Desktop Pet Memory\n\n- The user likes tea.\n";

    [Fact]
    public async Task NewChatSummarizesAndRestartReadsMemoryIntoNextReply()
    {
        using var files = new TestFiles();
        var summaries = 0;
        using (var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (IsSummary(body))
            {
                summaries++;
                return Ok(MemoryDocument);
            }
            return Ok("Noted");
        }))
        {
            await runtime.SendAsync("I like tea", default);
            Assert.Equal(0, summaries);
            Assert.False(File.Exists(files.Memory.DocumentPath));
            runtime.Clear();
            await runtime.EndConversationAsync();
            Assert.Equal(1, summaries);
            Assert.Single(runtime.Entries);
            Assert.Empty(runtime.Session.Messages);
            Assert.Equal(MemoryDocument, files.Memory.LoadDocument());
        }

        using var restarted = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.False(IsSummary(body));
            var messages = body.RootElement.GetProperty("messages");
            Assert.Equal(4, messages.GetArrayLength());
            Assert.Contains(JsonSerializer.Serialize(MemoryDocument), messages[1].GetProperty("content").GetString());
            Assert.Equal("What do I like?", messages[3].GetProperty("content").GetString());
            return Ok("Tea");
        });
        await restarted.SendAsync("What do I like?", default);
        Assert.Equal("Tea", restarted.Entries[^1].Text);
    }

    [Fact]
    public async Task FailedAndCancelledTurnsNeverEnterMemorySummary()
    {
        using var files = new TestFiles();
        using var cancellation = new CancellationTokenSource();
        var count = 0;
        using var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (IsSummary(body))
            {
                using var data = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
                var conversation = data.RootElement.GetProperty("conversation");
                Assert.Equal(2, conversation.GetArrayLength());
                Assert.Equal("I like tea", conversation[0].GetProperty("Content").GetString());
                return Ok(MemoryDocument);
            }
            switch (++count)
            {
                case 1: return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                case 2:
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    break;
            }
            return Ok("Noted");
        });
        await Assert.ThrowsAsync<ChatServiceException>(() => runtime.SendAsync("failed", default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.SendAsync("cancelled", cancellation.Token));
        await runtime.SendAsync("I like tea", default);
        await runtime.EndConversationAsync();
        Assert.Equal(MemoryDocument, files.Memory.LoadDocument());
    }

    [Fact]
    public async Task SummaryFailureDoesNotFailChatAndStartupRetriesPendingWork()
    {
        using var files = new TestFiles();
        using (var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            return IsSummary(body) ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok("Noted");
        }))
        {
            await runtime.SendAsync("I like tea", default);
            await runtime.EndConversationAsync();
            Assert.NotNull(runtime.MemoryError);
            Assert.Equal(3, runtime.Entries.Count);
            Assert.Single(files.Memory.LoadPending());
        }

        using var restarted = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            return Ok(IsSummary(body) ? MemoryDocument : "Tea");
        });
        await restarted.SendAsync("What do I like?", default);
        Assert.Equal(MemoryDocument, files.Memory.LoadDocument());
        Assert.Null(restarted.MemoryError);
        Assert.Empty(files.Memory.LoadPending());
    }

    [Fact]
    public async Task DisposeDuringSummaryCancelsWorkAndPreservesPendingConversation()
    {
        using var files = new TestFiles();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (IsSummary(body))
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            return Ok("Noted");
        });
        await runtime.SendAsync("I like tea", default);
        var summary = runtime.EndConversationAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Dispose();
        await summary.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(files.Memory.LoadPending());
        Assert.False(File.Exists(files.Memory.DocumentPath));
    }

    [Fact]
    public async Task ClosingFullChatAndBubbleSummarizesOnceWithoutClearingVisibleHistory()
    {
        await RunOnSta(async () =>
        {
            using var files = new TestFiles();
            var summaries = 0;
            using var runtime = files.CreateRuntime(async (request, token) =>
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                if (IsSummary(body)) summaries++;
                return Ok(IsSummary(body) ? MemoryDocument : "Noted");
            });
            await runtime.SendAsync("I like tea", default);
            var full = new ChatWindow(runtime);
            var bubble = new BubbleChatWindow(runtime);
            try
            {
                full.Show();
                full.Close();
                await runtime.EndConversationAsync();
                Assert.False(full.IsVisible);
                Assert.Equal(1, summaries);
                Assert.Equal(3, runtime.Entries.Count);

                bubble.Show();
                bubble.Close();
                await runtime.EndConversationAsync();
                Assert.Equal(1, summaries);
                Assert.Equal(MemoryDocument, files.Memory.LoadDocument());
            }
            finally
            {
                full.CloseForExit();
                bubble.CloseForExit();
            }
        });
    }

    private static bool IsSummary(JsonDocument body) => body.RootElement.GetProperty("max_tokens").GetInt32() == 4096;

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = text } } } }),
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
        private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"pet-memory-integration-{Guid.NewGuid():N}.bin");
        public ChatMemoryStore Memory { get; } = new(Path.Combine(Path.GetTempPath(), $"pet-memory-integration-{Guid.NewGuid():N}.md"));

        public ChatRuntime CreateRuntime(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            var store = new ChatSettingsStore(_settingsPath);
            store.Save(new ChatSettings { ApiKey = "memory-test-key" });
            return new ChatRuntime(new HttpClient(new Handler(respond)), store, Memory);
        }

        public void Dispose()
        {
            File.Delete(_settingsPath);
            File.Delete(Memory.DocumentPath);
            File.Delete(Memory.PendingPath);
            File.Delete(_settingsPath + ".emotion.json");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
}
