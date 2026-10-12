using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.Core;

namespace DesktopPet.Core.Tests;

public sealed class ChatMemoryTests
{
    private static ChatSettings Settings => new() { ApiKey = "test-memory-key" };
    private const string Saved = "# Desktop Pet Memory\n\n- The user likes tea.\n";

    [Fact]
    public async Task RecordsSuccessfulTurnsOnlyWhenConversationEndsAndDoesNotResummarizeThem()
    {
        using var files = new MemoryFiles();
        var requests = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            requests++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(4096, body.RootElement.GetProperty("max_tokens").GetInt32());
            var messages = body.RootElement.GetProperty("messages");
            Assert.Equal(2, messages.GetArrayLength());
            using var data = JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
            Assert.Equal("I like tea", data.RootElement.GetProperty("conversation")[0].GetProperty("Content").GetString());
            Assert.Equal("Noted", data.RootElement.GetProperty("conversation")[1].GetProperty("Content").GetString());
            Assert.Equal(ChatMemoryStore.EmptyDocument, data.RootElement.GetProperty("existing_memory").GetString());
            Assert.True(data.RootElement.TryGetProperty("ended_at", out _));
            return Ok(Saved);
        }));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("I like tea", "Noted");
        Assert.Equal(0, requests);
        Assert.False(File.Exists(files.Store.DocumentPath));

        await memory.EndConversationAsync(Settings, default);
        await memory.EndConversationAsync(Settings, default);

        Assert.Equal(1, requests);
        Assert.Equal(Saved, files.Store.LoadDocument());
        Assert.Empty(files.Store.LoadPending());
        Assert.Null(memory.LastError);
    }

    [Fact]
    public async Task LongConversationIncludesTurnsBeyondTheChatContextLimit()
    {
        using var files = new MemoryFiles();
        var remembered = new List<string>();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            using var data = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            remembered.AddRange(data.RootElement.GetProperty("conversation").EnumerateArray()
                .Where(message => message.GetProperty("Role").GetString() == "user")
                .Select(message => message.GetProperty("Content").GetString()!));
            return Ok(Saved);
        }));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        for (var i = 0; i < 25; i++) memory.RecordTurn($"fact-{i}", "reply");

        await memory.EndConversationAsync(Settings, default);

        Assert.Equal(Enumerable.Range(0, 25).Select(i => $"fact-{i}"), remembered);
        Assert.Empty(files.Store.LoadPending());
    }

    [Fact]
    public async Task FailedSummaryPreservesDocumentAndRetriesAfterRestart()
    {
        using var files = new MemoryFiles();
        files.Store.SaveDocument(Saved, ChatMemoryStore.EmptyDocument);
        using var offline = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var memory = new ChatMemory(new DeepSeekChatClient(offline), files.Store);
        memory.RecordTurn("I now prefer coffee", "Understood");
        await memory.EndConversationAsync(Settings, default);
        Assert.NotNull(memory.LastError);
        Assert.Equal(Saved, files.Store.LoadDocument());
        Assert.Equal("I now prefer coffee", Assert.Single(files.Store.LoadPending()).Messages[0].Content);

        const string updated = "# Desktop Pet Memory\n\n- The user prefers coffee.\n";
        using var online = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(updated))));
        var restarted = new ChatMemory(new DeepSeekChatClient(online), files.Store);
        await restarted.RetryPendingAsync(Settings, default);
        Assert.Equal(updated, files.Store.LoadDocument());
        Assert.Empty(files.Store.LoadPending());
        Assert.Null(restarted.LastError);
    }

    [Fact]
    public async Task ConcurrentEndRequestsSummarizeEachTurnOnce()
    {
        using var files = new MemoryFiles();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            requests++;
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return Ok(Saved);
        }));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("I like tea", "Noted");
        var first = memory.EndConversationAsync(Settings, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = memory.EndConversationAsync(Settings, default);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, requests);
        Assert.Empty(files.Store.LoadPending());
    }

    [Fact]
    public async Task ManualEditDuringSummaryIsPreservedWithPendingConversation()
    {
        using var files = new MemoryFiles();
        using var http = new HttpClient(new Handler((_, _) =>
        {
            files.Store.SaveDocument("# Desktop Pet Memory\n\n- Manual edit.\n", ChatMemoryStore.EmptyDocument);
            return Task.FromResult(Ok(Saved));
        }));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("I like tea", "Noted");
        await memory.EndConversationAsync(Settings, default);
        Assert.IsType<IOException>(memory.LastError);
        Assert.Contains("Manual edit", files.Store.LoadDocument());
        Assert.Single(files.Store.LoadPending());
    }

    [Theory]
    [InlineData("reply", null)]
    [InlineData("# Desktop Pet Memory\n\n- Incomplete", "length")]
    [InlineData("", null)]
    public async Task InvalidOrTruncatedSummaryNeverOverwritesMemory(string answer, string? finishReason)
    {
        using var files = new MemoryFiles();
        files.Store.SaveDocument(Saved, ChatMemoryStore.EmptyDocument);
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Ok(answer, finishReason))));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("new fact", "reply");
        await memory.EndConversationAsync(Settings, default);
        Assert.NotNull(memory.LastError);
        Assert.Equal(Saved, files.Store.LoadDocument());
        Assert.Single(files.Store.LoadPending());
    }

    [Fact]
    public async Task CancellationRetainsPendingConversationForRetry()
    {
        using var files = new MemoryFiles();
        using var source = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            source.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Ok(Saved);
        }));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("new fact", "reply");
        await memory.EndConversationAsync(Settings, source.Token);
        Assert.IsAssignableFrom<OperationCanceledException>(memory.LastError);
        Assert.Single(files.Store.LoadPending());
        Assert.False(File.Exists(files.Store.DocumentPath));
    }

    [Fact]
    public async Task CorruptPendingFileIsNotOverwritten()
    {
        using var files = new MemoryFiles();
        File.WriteAllText(files.Store.PendingPath, "broken data");
        using var http = new HttpClient(new Handler((_, _) => throw new Exception("Must not call API")));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("new fact", "reply");
        await memory.EndConversationAsync(Settings, default);
        Assert.IsType<InvalidDataException>(memory.LastError);
        Assert.Equal("broken data", File.ReadAllText(files.Store.PendingPath));
    }

    [Fact]
    public async Task MissingApiKeyPersistsPendingWorkWithoutSending()
    {
        using var files = new MemoryFiles();
        using var http = new HttpClient(new Handler((_, _) => throw new Exception("Must not call API")));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("new fact", "reply");
        await memory.EndConversationAsync(new ChatSettings(), default);
        Assert.Single(files.Store.LoadPending());
    }

    [Fact]
    public void EditorReturnsEmptyDocumentForMissingFileButReportsCorruptStorage()
    {
        using var files = new MemoryFiles();
        using var http = new HttpClient(new Handler((_, _) => throw new Exception("Must not call API")));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        Assert.Equal(ChatMemoryStore.EmptyDocument, memory.LoadDocumentForEditing());
        Assert.False(File.Exists(files.Store.DocumentPath));
        File.WriteAllText(files.Store.DocumentPath, "broken data");
        Assert.Throws<InvalidDataException>(() => memory.LoadDocumentForEditing());
        File.WriteAllText(files.Store.DocumentPath, new string('x', ChatMemoryStore.MaxDocumentLength + 1));
        Assert.Throws<InvalidDataException>(() => memory.LoadDocumentForEditing());
    }

    [Fact]
    public async Task ExplicitEditDiscardsDurablePendingAndCurrentTurnsAndKeepsExactFormatting()
    {
        using var files = new MemoryFiles();
        files.Store.SaveDocument(Saved, ChatMemoryStore.EmptyDocument);
        using var http = new HttpClient(new Handler((_, _) => throw new Exception("Forgotten turns must not be summarized")));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("old tea preference", "Noted");
        await memory.EndConversationAsync(new ChatSettings(), default);
        memory.RecordTurn("a second old fact", "Noted");
        const string edited = "# Desktop Pet Memory\r\n\r\n## 手动备注\r\n  保留两处空格。  \r\n";
        memory.SaveEditedDocument(edited, memory.LoadDocumentForEditing());
        await memory.EndConversationAsync(Settings, default);
        Assert.Equal(edited, files.Store.LoadDocument());
        Assert.Empty(files.Store.LoadPending());
        var restarted = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        await restarted.RetryPendingAsync(Settings, default);
        Assert.Null(restarted.LastError);
    }

    [Fact]
    public void StaleEditorCannotOverwriteExternalEditOrDiscardPendingWork()
    {
        using var files = new MemoryFiles();
        files.Store.SaveDocument(Saved, ChatMemoryStore.EmptyDocument);
        files.Store.SavePending([new(DateTimeOffset.UtcNow, [new("user", "old fact"), new("assistant", "Noted")])]);
        using var http = new HttpClient(new Handler((_, _) => throw new Exception("Must not call API")));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        var original = memory.LoadDocumentForEditing();
        const string external = "# Desktop Pet Memory\n\n- Edited in another app.\n";
        File.WriteAllText(files.Store.DocumentPath, external);
        Assert.Throws<IOException>(() => memory.SaveEditedDocument(ChatMemoryStore.EmptyDocument, original));
        Assert.Equal(external, files.Store.LoadDocument());
        Assert.Single(files.Store.LoadPending());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightSummaryCannotCommitAfterExplicitSaveEvenWhenContentIsUnchanged(bool unchanged)
    {
        using var files = new MemoryFiles();
        files.Store.SaveDocument(Saved, ChatMemoryStore.EmptyDocument);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var http = new HttpClient(new Handler(async (_, _) =>
        {
            requests++;
            entered.TrySetResult();
            await release.Task;
            return Ok("# Desktop Pet Memory\n\n- Old deleted fact returned.\n");
        }));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("old fact", "Noted");
        var work = memory.EndConversationAsync(Settings, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var edited = unchanged ? Saved : ChatMemoryStore.EmptyDocument;
        memory.SaveEditedDocument(edited, Saved);
        release.SetResult();
        await work;
        await memory.RetryPendingAsync(Settings, default);
        Assert.Equal(edited, files.Store.LoadDocument());
        Assert.Empty(files.Store.LoadPending());
        Assert.Equal(1, requests);
        Assert.Null(memory.LastError);
    }

    [Fact]
    public async Task PauseRejectsLateSummaryPreservesPendingAndSkipsNewTurnsUntilResume()
    {
        using var files = new MemoryFiles();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            requests++;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.DoesNotContain("paused fact", json.RootElement.GetRawText());
            if (requests == 1)
            {
                entered.TrySetResult();
                await release.Task;
            }
            return Ok(Saved);
        }));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("I like tea", "Noted");
        var work = memory.EndConversationAsync(Settings, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        memory.SetRecordingEnabled(false);
        memory.RecordTurn("paused fact", "Paused reply");
        release.SetResult();
        await work;
        await memory.RetryPendingAsync(Settings, default);
        await memory.EndConversationAsync(Settings, default);
        Assert.Equal(1, requests);
        Assert.False(File.Exists(files.Store.DocumentPath));
        Assert.Single(files.Store.LoadPending());
        memory.SetRecordingEnabled(true);
        await memory.RetryPendingAsync(Settings, default);
        await memory.EndConversationAsync(Settings, default);
        Assert.Equal(2, requests);
        Assert.Equal(Saved, files.Store.LoadDocument());
        Assert.Empty(files.Store.LoadPending());
    }

    [Fact]
    public async Task NewTurnsAfterAnExplicitEditRemainRecordable()
    {
        using var files = new MemoryFiles();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            var body = await request.Content!.ReadAsStringAsync(token);
            Assert.Contains("new fact", body);
            Assert.DoesNotContain("old fact", body);
            return Ok(Saved);
        }));
        var memory = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        memory.RecordTurn("old fact", "Noted");
        memory.SaveEditedDocument(ChatMemoryStore.EmptyDocument, ChatMemoryStore.EmptyDocument);
        memory.RecordTurn("new fact", "Noted");
        await memory.EndConversationAsync(Settings, default);
        Assert.Equal(Saved, files.Store.LoadDocument());
        Assert.Null(memory.LastError);
    }

    [Fact]
    public async Task SavesUsingDifferentStoreInstancesSerializeExpectedDocumentCheck()
    {
        using var files = new MemoryFiles();
        var second = new ChatMemoryStore(files.Store.DocumentPath);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(() =>
        {
            try
            {
                (index == 0 ? files.Store : second).SaveDocument($"# Desktop Pet Memory\n\n- Writer {index}.\n", ChatMemoryStore.EmptyDocument);
                return true;
            }
            catch (IOException) { return false; }
        })));
        Assert.Single(outcomes.Where(saved => saved));
        Assert.Single(outcomes.Where(saved => !saved));
    }

    [Fact]
    public async Task EditFromAnotherMemoryInstanceInvalidatesItsInFlightAndQueuedOldFacts()
    {
        using var files = new MemoryFiles();
        files.Store.SaveDocument(Saved, ChatMemoryStore.EmptyDocument);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var http = new HttpClient(new Handler(async (_, _) =>
        {
            requests++;
            entered.TrySetResult();
            await release.Task;
            return Ok("# Desktop Pet Memory\n\n- Reintroduced old fact.\n");
        }));
        var first = new ChatMemory(new DeepSeekChatClient(http), files.Store);
        var editor = new ChatMemory(new DeepSeekChatClient(http), new ChatMemoryStore(files.Store.DocumentPath));
        first.RecordTurn("old fact", "Noted");
        var work = first.EndConversationAsync(Settings, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        first.RecordTurn("another old fact", "Noted");
        editor.SaveEditedDocument(Saved, Saved);
        release.SetResult();
        await work;
        await first.EndConversationAsync(Settings, default);
        await first.RetryPendingAsync(Settings, default);
        Assert.Equal(1, requests);
        Assert.Equal(Saved, files.Store.LoadDocument());
        Assert.Empty(files.Store.LoadPending());
        Assert.Null(first.LastError);
    }

    private static HttpResponseMessage Ok(string text, string? finishReason = "stop") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = text }, finish_reason = finishReason } }
        }), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }

    private sealed class MemoryFiles : IDisposable
    {
        public ChatMemoryStore Store { get; } = new(Path.Combine(Path.GetTempPath(), $"pet-memory-{Guid.NewGuid():N}.md"));

        public void Dispose()
        {
            File.Delete(Store.DocumentPath);
            File.Delete(Store.PendingPath);
            File.Delete(Store.DocumentPath + ".tmp");
            File.Delete(Store.PendingPath + ".tmp");
        }
    }
}
