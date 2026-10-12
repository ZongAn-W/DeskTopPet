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

public sealed class MemoryWindowTests
{
    private const string InitialDocument = "# Desktop Pet Memory\n\n## 偏好\n\n- 喜欢 Tea。\n- 喜欢安静。\n\n## 计划\n\n- 明天做演示。\n";

    [Fact]
    public Task ChatMemoryButtonOpensOneOwnedPanelAndCanReopenAfterClosing() => RunOnSta(() =>
    {
        using var files = new TestFiles(InitialDocument);
        using var runtime = files.CreateRuntime(NoNetwork);
        var chat = new ChatWindow(runtime);
        try
        {
            chat.Show();
            Click(chat, "MemoryButton");
            var memory = Assert.Single(chat.OwnedWindows.OfType<MemoryWindow>());
            Assert.True(memory.IsVisible);
            Assert.Same(chat, memory.Owner);
            Assert.Equal(3, Notes(memory).Length);

            Click(chat, "MemoryButton");
            Assert.Same(memory, Assert.Single(chat.OwnedWindows.OfType<MemoryWindow>()));
            Assert.Single(runtime.Entries);
            Assert.Empty(runtime.Session.Messages);

            memory.CloseForExit();
            Click(chat, "MemoryButton");
            Assert.NotSame(memory, Assert.Single(chat.OwnedWindows.OfType<MemoryWindow>()));
        }
        finally { chat.CloseForExit(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SearchFiltersContentAndSectionAndClearsAnUnmatchedSelection() => RunOnSta(() =>
    {
        using var files = new TestFiles(InitialDocument);
        using var runtime = files.CreateRuntime(NoNetwork);
        var window = new MemoryWindow(runtime);
        try
        {
            Control<TextBox>(window, "SearchBox").Text = "tea";
            var tea = Assert.Single(Notes(window));
            Assert.Equal("喜欢 Tea。", tea.Text);
            Assert.Equal("找到 1 条", Control<TextBlock>(window, "CountText").Text);
            Control<ListBox>(window, "NotesList").SelectedItem = tea;
            Assert.Equal(Visibility.Visible, Control<Grid>(window, "EditorPanel").Visibility);

            Control<TextBox>(window, "SearchBox").Text = "计划";
            Assert.Equal("明天做演示。", Assert.Single(Notes(window)).Text);
            Assert.Null(Control<ListBox>(window, "NotesList").SelectedItem);
            Assert.Equal(Visibility.Collapsed, Control<Grid>(window, "EditorPanel").Visibility);

            Control<TextBox>(window, "SearchBox").Text = "没有这样的记忆";
            Assert.Empty(Notes(window));
            Assert.Equal(Visibility.Visible, Control<TextBlock>(window, "EmptyText").Visibility);
            Control<TextBox>(window, "SearchBox").Clear();
            Assert.Equal(3, Notes(window).Length);
            Assert.Equal("共 3 条", Control<TextBlock>(window, "CountText").Text);
            Assert.Equal(InitialDocument, files.Memory.LoadDocument());
        }
        finally { window.CloseForExit(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditingAddingAndDeletingIndividualNotesOnlyChangesDiskAfterSave() => RunOnSta(() =>
    {
        using var files = new TestFiles(InitialDocument);
        using var runtime = files.CreateRuntime(NoNetwork);
        var window = new MemoryWindow(runtime);
        try
        {
            Select(window, "喜欢 Tea。");
            Control<TextBox>(window, "NoteTextBox").Text = "喜欢乌龙茶。\n下午饮用。";
            Assert.True(Control<Button>(window, "SaveButton").IsEnabled);
            Assert.Equal(InitialDocument, files.Memory.LoadDocument());
            Click(window, "SaveButton");
            var edited = MemoryDocument.Parse(files.Memory.LoadDocument());
            Assert.Contains(edited.Notes, note => note.Section == "偏好" && note.Text == "喜欢乌龙茶。\n下午饮用。");
            Assert.Contains(edited.Notes, note => note.Text == "喜欢安静。");
            Assert.False(Control<Button>(window, "SaveButton").IsEnabled);

            var beforeAdd = files.Memory.LoadDocument();
            Click(window, "AddButton");
            Control<ComboBox>(window, "SectionBox").Text = "事实";
            Control<TextBox>(window, "NoteTextBox").Text = "昵称是小安。";
            Assert.Equal(beforeAdd, files.Memory.LoadDocument());
            Click(window, "SaveButton");
            Assert.Equal(4, Notes(window).Length);
            Assert.Contains(MemoryDocument.Parse(files.Memory.LoadDocument()).Notes,
                note => note.Section == "事实" && note.Text == "昵称是小安。");

            var beforeDelete = files.Memory.LoadDocument();
            Select(window, "明天做演示。");
            Click(window, "DeleteButton");
            Assert.Equal(beforeDelete, files.Memory.LoadDocument());
            Assert.Equal(3, Notes(window).Length);
            Click(window, "SaveButton");
            Assert.DoesNotContain(MemoryDocument.Parse(files.Memory.LoadDocument()).Notes,
                note => note.Text == "明天做演示。");
            Assert.Empty(files.Memory.LoadPending());
        }
        finally { window.CloseForExit(); }

        var reopened = new MemoryWindow(runtime);
        try
        {
            Assert.Equal(3, Notes(reopened).Length);
            Assert.Contains(Notes(reopened), note => note.Text == "昵称是小安。");
        }
        finally { reopened.CloseForExit(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SwitchingNotesKeepsEditsAttachedToTheirOriginalNote() => RunOnSta(() =>
    {
        using var files = new TestFiles(InitialDocument);
        using var runtime = files.CreateRuntime(NoNetwork);
        var window = new MemoryWindow(runtime);
        try
        {
            Select(window, "喜欢 Tea。");
            Control<TextBox>(window, "NoteTextBox").Text = "更喜欢咖啡。";
            Select(window, "喜欢安静。");
            Assert.Equal("喜欢安静。", Control<TextBox>(window, "NoteTextBox").Text);
            Control<TextBox>(window, "NoteTextBox").Text = "喜欢安静的晚上。";
            Click(window, "SaveButton");

            var notes = MemoryDocument.Parse(files.Memory.LoadDocument()).Notes;
            Assert.Equal(3, notes.Count);
            Assert.Equal("更喜欢咖啡。", notes[0].Text);
            Assert.Equal("喜欢安静的晚上。", notes[1].Text);
            Assert.Equal("明天做演示。", notes[2].Text);
        }
        finally { window.CloseForExit(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ConflictingSavePreservesExternalDocumentAndUnsavedEditorText() => RunOnSta(() =>
    {
        using var files = new TestFiles(InitialDocument);
        using var runtime = files.CreateRuntime(NoNetwork);
        var window = new MemoryWindow(runtime);
        try
        {
            Select(window, "喜欢 Tea。");
            Control<TextBox>(window, "NoteTextBox").Text = "面板里的修改。";
            const string external = "# Desktop Pet Memory\n\n- 外部编辑刚刚增加的记忆。\n";
            File.WriteAllText(files.Memory.DocumentPath, external, new UTF8Encoding(false));
            files.Memory.SavePending([new PendingMemoryConversation(DateTimeOffset.UtcNow,
                [new ChatMessage("user", "等待整理"), new ChatMessage("assistant", "收到")])]);

            Click(window, "SaveButton");

            Assert.Equal(external, files.Memory.LoadDocument());
            Assert.Equal("面板里的修改。", Control<TextBox>(window, "NoteTextBox").Text);
            Assert.True(Control<Button>(window, "SaveButton").IsEnabled);
            Assert.Contains("保存未完成", Control<TextBlock>(window, "StatusText").Text);
            Assert.Single(files.Memory.LoadPending());
        }
        finally { window.CloseForExit(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RecordingTogglePersistsAndPausedChatStillReadsExistingNotes() => RunOnSta(async () =>
    {
        using var files = new TestFiles(InitialDocument);
        var summaries = 0;
        using (var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (IsSummary(body)) summaries++;
            Assert.False(IsSummary(body));
            var messages = body.RootElement.GetProperty("messages");
            Assert.Contains(JsonSerializer.Serialize(InitialDocument), messages[1].GetProperty("content").GetString());
            return Ok("仍然记得你喜欢茶。");
        }))
        {
            var window = new MemoryWindow(runtime);
            try
            {
                Control<CheckBox>(window, "RecordingCheckBox").IsChecked = false;
                Assert.False(runtime.MemoryRecordingEnabled);
                Assert.False(files.Settings.Load().MemoryRecordingEnabled);
                Assert.Contains("已暂停", Control<TextBlock>(window, "RecordingHelp").Text);
                await runtime.SendAsync("这轮不要记下", default);
                await runtime.EndConversationAsync();
                Assert.Equal(0, summaries);
                Assert.Empty(files.Memory.LoadPending());
                Assert.Equal(InitialDocument, files.Memory.LoadDocument());
            }
            finally { window.CloseForExit(); }
        }

        using var restarted = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (!IsSummary(body)) return Ok("收到。");
            summaries++;
            using var data = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var conversation = data.RootElement.GetProperty("conversation");
            Assert.Equal(2, conversation.GetArrayLength());
            Assert.Equal("恢复后可以记下", conversation[0].GetProperty("Content").GetString());
            return Ok(InitialDocument);
        });
        var reopened = new MemoryWindow(restarted);
        try
        {
            Assert.False(restarted.MemoryRecordingEnabled);
            Assert.False(Control<CheckBox>(reopened, "RecordingCheckBox").IsChecked);
            Control<CheckBox>(reopened, "RecordingCheckBox").IsChecked = true;
            await restarted.SendAsync("恢复后可以记下", default);
            await restarted.EndConversationAsync();
            Assert.Equal(1, summaries);
            Assert.True(files.Settings.Load().MemoryRecordingEnabled);
        }
        finally { reopened.CloseForExit(); }
    });

    [Fact]
    public Task SavingAiSettingsKeepsRecordingPaused() => RunOnSta(() =>
    {
        using var files = new TestFiles(InitialDocument, recordingEnabled: false);
        using var runtime = files.CreateRuntime(NoNetwork);
        var memory = new MemoryWindow(runtime);
        var settings = new ChatSettingsWindow(runtime.Settings, files.Settings);
        try
        {
            Control<ComboBox>(settings, "ModelBox").Text = "test-model-changed";
            Control<TextBox>(settings, "PersonaBox").Text = "新的陪伴设定。";
            Click(settings, "SaveButton");

            var saved = Assert.IsType<ChatSettings>(settings.SavedSettings);
            Assert.False(saved.MemoryRecordingEnabled);
            Assert.False(files.Settings.Load().MemoryRecordingEnabled);
            runtime.SaveSettings(saved);
            Assert.False(runtime.MemoryRecordingEnabled);
            Assert.False(Control<CheckBox>(memory, "RecordingCheckBox").IsChecked);
            Assert.Equal("test-model-changed", runtime.Settings.Model);
            Assert.Equal("新的陪伴设定。", runtime.Settings.Persona);
        }
        finally { settings.Close(); memory.CloseForExit(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ManualSaveDuringReplySkipsLateTurnInLongTermMemory() => RunOnSta(async () =>
    {
        using var files = new TestFiles(InitialDocument);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var summaries = 0;
        using var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (IsSummary(body)) { summaries++; return Ok(InitialDocument); }
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        });
        var window = new MemoryWindow(runtime);
        try
        {
            var send = runtime.SendAsync("这条仍在等待回复", default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Select(window, "喜欢 Tea。");
            Control<TextBox>(window, "NoteTextBox").Text = "喜欢花茶。";
            Click(window, "SaveButton");
            var edited = files.Memory.LoadDocument();
            release.SetResult(Ok("迟到但仍然显示的回复。"));
            await send;
            await runtime.EndConversationAsync();

            Assert.Equal("迟到但仍然显示的回复。", runtime.Entries[^1].Text);
            Assert.Equal(2, runtime.Session.Messages.Count);
            Assert.Equal(0, summaries);
            Assert.Equal(edited, files.Memory.LoadDocument());
            Assert.Empty(files.Memory.LoadPending());
        }
        finally { release.TrySetResult(Ok("清理回复。")); window.CloseForExit(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ChangingRecordingDuringReplyDoesNotRecordTheLateTurn(bool initiallyEnabled) => RunOnSta(async () =>
    {
        using var files = new TestFiles(InitialDocument, initiallyEnabled);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var summaries = 0;
        using var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (IsSummary(body)) { summaries++; return Ok(InitialDocument); }
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        });
        var window = new MemoryWindow(runtime);
        try
        {
            var send = runtime.SendAsync("改变记录开关前的消息", default);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var toggle = Control<CheckBox>(window, "RecordingCheckBox");
            if (initiallyEnabled) toggle.IsChecked = false;
            toggle.IsChecked = true;
            release.SetResult(Ok("正常显示，跳过记忆。"));
            await send;
            await runtime.EndConversationAsync();

            Assert.True(runtime.MemoryRecordingEnabled);
            Assert.Equal("正常显示，跳过记忆。", runtime.Entries[^1].Text);
            Assert.Equal(0, summaries);
            Assert.Equal(InitialDocument, files.Memory.LoadDocument());
            Assert.Empty(files.Memory.LoadPending());
        }
        finally { release.TrySetResult(Ok("清理回复。")); window.CloseForExit(); }
    });

    [Fact]
    public Task ManualSaveWhileSendWaitsForSummaryInvalidatesBothOldSummaryAndWaitingTurn() => RunOnSta(async () =>
    {
        using var files = new TestFiles(InitialDocument);
        files.Memory.SavePending([new PendingMemoryConversation(DateTimeOffset.UtcNow,
            [new ChatMessage("user", "应该忘记的旧内容"), new ChatMessage("assistant", "旧回复")])]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var summaries = 0;
        using var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (!IsSummary(body)) return Ok("等待结束后的聊天回复。");
            summaries++;
            entered.TrySetResult();
            return await release.Task.WaitAsync(token);
        });
        var window = new MemoryWindow(runtime);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var send = runtime.SendAsync("排在旧整理后面的新消息", default);
            Assert.True(runtime.IsBusy);
            Select(window, "喜欢 Tea。");
            Control<TextBox>(window, "NoteTextBox").Text = "这条是手动确认的记忆。";
            Click(window, "SaveButton");
            var edited = files.Memory.LoadDocument();
            release.SetResult(Ok("# Desktop Pet Memory\n\n- 应该忘记的旧内容\n"));
            await send;
            await runtime.EndConversationAsync();

            Assert.Equal(1, summaries);
            Assert.Equal("等待结束后的聊天回复。", runtime.Entries[^1].Text);
            Assert.Equal(edited, files.Memory.LoadDocument());
            Assert.DoesNotContain("应该忘记", edited);
            Assert.Empty(files.Memory.LoadPending());
            Assert.Null(runtime.MemoryError);
        }
        finally { release.TrySetResult(Ok(InitialDocument)); window.CloseForExit(); }
    });

    private static T Control<T>(FrameworkElement window, string name) where T : class => Assert.IsType<T>(window.FindName(name));

    private static MemoryNote[] Notes(MemoryWindow window) => Control<ListBox>(window, "NotesList").Items.Cast<MemoryNote>().ToArray();

    private static void Select(MemoryWindow window, string text) =>
        Control<ListBox>(window, "NotesList").SelectedItem = Notes(window).Single(note => note.Text == text);

    private static void Click(FrameworkElement window, string name)
    {
        var button = Control<Button>(window, name);
        Assert.True(button.IsEnabled);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static bool IsSummary(JsonDocument body) => body.RootElement.GetProperty("max_tokens").GetInt32() == 4096;

    private static Task<HttpResponseMessage> NoNetwork(HttpRequestMessage request, CancellationToken token) =>
        throw new InvalidOperationException("This local memory-panel operation must not make an API request.");

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
        private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"pet-memory-window-{Guid.NewGuid():N}.bin");
        public ChatSettingsStore Settings { get; }
        public ChatMemoryStore Memory { get; } = new(Path.Combine(Path.GetTempPath(), $"pet-memory-window-{Guid.NewGuid():N}.md"));

        public TestFiles(string document, bool recordingEnabled = true)
        {
            Settings = new ChatSettingsStore(_settingsPath);
            Settings.Save(new ChatSettings { ApiKey = "fake-memory-panel-test-key-never-used-online", MemoryRecordingEnabled = recordingEnabled });
            Memory.SaveDocument(document, ChatMemoryStore.EmptyDocument);
        }

        public ChatRuntime CreateRuntime(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            new(new HttpClient(new Handler(respond)), Settings, Memory);

        public void Dispose()
        {
            File.Delete(_settingsPath);
            File.Delete(_settingsPath + ".tmp");
            File.Delete(_settingsPath + ".emotion.json");
            File.Delete(_settingsPath + ".emotion.json.tmp");
            File.Delete(Memory.DocumentPath);
            File.Delete(Memory.DocumentPath + ".tmp");
            File.Delete(Memory.PendingPath);
            File.Delete(Memory.PendingPath + ".tmp");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }
}
