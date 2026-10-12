using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet.Windows.Tests;

public sealed class MemoryPanelTests
{
    private const string Original = "# Desktop Pet Memory\r\n\r\n## 偏好\r\n\r\n* 喜欢茶。\r\n\r\n## 计划\r\n- 周五去旅行。\r\n";

    [Fact]
    public void PanelEditsReclassifiesSearchesAndSavesWithoutAnApiRequest() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        files.Memory.SaveDocument(Original, ChatMemoryStore.EmptyDocument);
        using var runtime = files.CreateRuntime();
        var window = new MemoryWindow(runtime);
        try
        {
            window.Show();
            var list = Control<ListBox>(window, "NotesList");
            list.SelectedIndex = 0;
            Control<TextBox>(window, "NoteTextBox").Text = "喜欢咖啡。\n不加糖。";
            list.SelectedIndex = 1;
            Assert.Equal("周五去旅行。", Control<TextBox>(window, "NoteTextBox").Text);
            Control<ComboBox>(window, "SectionBox").Text = "事实";
            Click(window, "SaveButton");
            var saved = MemoryDocument.Parse(files.Memory.LoadDocument());
            Assert.Equal("喜欢咖啡。\n不加糖。", saved.Notes[0].Text);
            Assert.Equal("事实", saved.Notes[1].Section);
            Assert.Contains("* 喜欢咖啡。\r\n", saved.Source);
            Control<TextBox>(window, "SearchBox").Text = "咖啡";
            Assert.Single(list.Items.Cast<MemoryNote>());
            Control<TextBox>(window, "SearchBox").Text = "不存在";
            Assert.Empty(list.Items.Cast<MemoryNote>());
            Assert.Equal(Visibility.Visible, Control<TextBlock>(window, "EmptyText").Visibility);
            Assert.Equal(0, files.Requests);
        }
        finally { window.CloseForExit(); }
    });

    [Fact]
    public void AddAndDeleteRemainDraftsUntilSaved() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        using var runtime = files.CreateRuntime();
        var window = new MemoryWindow(runtime);
        try
        {
            window.Show();
            Click(window, "AddButton");
            Control<TextBox>(window, "NoteTextBox").Text = "正在学习摄影。";
            Assert.False(File.Exists(files.Memory.DocumentPath));
            Click(window, "SaveButton");
            Assert.Equal("正在学习摄影。", Assert.Single(MemoryDocument.Parse(files.Memory.LoadDocument()).Notes).Text);
            Assert.Empty(files.Memory.LoadPending());
            var list = Control<ListBox>(window, "NotesList");
            list.SelectedIndex = 0;
            Click(window, "DeleteButton");
            Assert.Contains("正在学习摄影。", files.Memory.LoadDocument());
            Click(window, "SaveButton");
            Assert.Empty(MemoryDocument.Parse(files.Memory.LoadDocument()).Notes);
            Assert.Equal(0, files.Requests);
        }
        finally { window.CloseForExit(); }
    });

    [Fact]
    public void ExternalEditConflictKeepsBothTheFileAndPanelDraft() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        files.Memory.SaveDocument(Original, ChatMemoryStore.EmptyDocument);
        using var runtime = files.CreateRuntime();
        var window = new MemoryWindow(runtime);
        try
        {
            window.Show();
            Control<ListBox>(window, "NotesList").SelectedIndex = 0;
            Control<TextBox>(window, "NoteTextBox").Text = "面板草稿。";
            files.Memory.SaveDocument("# Desktop Pet Memory\n\n- 外部编辑。\n", files.Memory.LoadDocument());
            Click(window, "SaveButton");
            Assert.Contains("外部编辑。", files.Memory.LoadDocument());
            Assert.Equal("面板草稿。", Control<TextBox>(window, "NoteTextBox").Text);
            Assert.Contains("保存未完成", Control<TextBlock>(window, "StatusText").Text);
            Assert.True(Control<Button>(window, "SaveButton").IsEnabled);
        }
        finally { window.CloseForExit(); }
    });

    [Fact]
    public void SelectingAnExistingDuplicateAfterAnEditKeepsTheClickedNoteSelected() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        files.Memory.SaveDocument("# Desktop Pet Memory\n\n## 偏好\n- A\n- B\n", ChatMemoryStore.EmptyDocument);
        using var runtime = files.CreateRuntime();
        var window = new MemoryWindow(runtime);
        try
        {
            var list = Control<ListBox>(window, "NotesList");
            list.SelectedIndex = 0;
            Control<TextBox>(window, "NoteTextBox").Text = "B";
            list.SelectedIndex = 1;
            Assert.Equal(1, Assert.IsType<MemoryNote>(list.SelectedItem).Id);
            Control<TextBox>(window, "NoteTextBox").Text = "C";
            Click(window, "SaveButton");
            Assert.Equal(new[] { "B", "C" }, MemoryDocument.Parse(files.Memory.LoadDocument()).Notes.Select(note => note.Text));
        }
        finally { window.CloseForExit(); }
    });

    [Fact]
    public void UnsupportedMarkdownKeepsTheDraftAndOriginalDocument() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        files.Memory.SaveDocument(Original, ChatMemoryStore.EmptyDocument);
        using var runtime = files.CreateRuntime();
        var window = new MemoryWindow(runtime);
        try
        {
            Control<ListBox>(window, "NotesList").SelectedIndex = 0;
            var draft = "喜欢茶。\n## 周末\n想去公园。";
            Control<TextBox>(window, "NoteTextBox").Text = draft;
            Click(window, "SaveButton");
            Assert.Equal(draft, Control<TextBox>(window, "NoteTextBox").Text);
            Assert.Contains("普通文字", Control<TextBlock>(window, "StatusText").Text);
            Assert.DoesNotContain("周末", files.Memory.LoadDocument());
            Assert.Equal(2, Control<ListBox>(window, "NotesList").Items.Count);
        }
        finally { window.CloseForExit(); }
    });

    [Fact]
    public void InvalidDocumentOffersRecoveryInsteadOfEditableEmptyMemory() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        File.WriteAllText(files.Memory.DocumentPath, "invalid document");
        using var runtime = files.CreateRuntime();
        var window = new MemoryWindow(runtime);
        try
        {
            Assert.False(Control<Button>(window, "AddButton").IsEnabled);
            Assert.False(Control<Button>(window, "SaveButton").IsEnabled);
            Assert.Contains("无法读取", Control<TextBlock>(window, "StatusText").Text);
            File.WriteAllText(files.Memory.DocumentPath, Original);
            Click(window, "ReloadButton");
            Assert.Equal(2, Control<ListBox>(window, "NotesList").Items.Count);
            Assert.True(Control<Button>(window, "AddButton").IsEnabled);
        }
        finally { window.CloseForExit(); }
    });

    [Fact]
    public void FullChatOpensOneMemoryPanelAndReusesIt() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        using var runtime = files.CreateRuntime();
        var chat = new ChatWindow(runtime);
        try
        {
            chat.Show();
            Click(chat, "MemoryButton");
            var panel = Assert.Single(chat.OwnedWindows.Cast<Window>());
            Assert.IsType<MemoryWindow>(panel);
            Assert.True(panel.IsVisible);
            Click(chat, "MemoryButton");
            Assert.Same(panel, Assert.Single(chat.OwnedWindows.Cast<Window>()));
        }
        finally { chat.CloseForExit(); }
    });

    [Fact]
    public void PausePersistsAndSavingAiSettingsPreservesIt() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        using var runtime = files.CreateRuntime();
        var panel = new MemoryWindow(runtime);
        try
        {
            Control<CheckBox>(panel, "RecordingCheckBox").IsChecked = false;
            Assert.False(runtime.MemoryRecordingEnabled);
            Assert.False(files.Settings.Load().MemoryRecordingEnabled);
            var settings = new ChatSettingsWindow(runtime.Settings, files.Settings);
            try
            {
                Control<TextBox>(settings, "PersonaBox").Text = "新性格。";
                Click(settings, "SaveButton");
                Assert.NotNull(settings.SavedSettings);
                Assert.False(settings.SavedSettings!.MemoryRecordingEnabled);
                Assert.False(files.Settings.Load().MemoryRecordingEnabled);
            }
            finally { settings.Close(); }
        }
        finally { panel.CloseForExit(); }
    });

    [Fact]
    public void CancellingMemoryPanelClosePreventsThePetExitSequence() => RunOnSta(() =>
    {
        using var files = new TestFiles();
        using var runtime = files.CreateRuntime();
        var chat = new ChatWindow(runtime);
        var pet = new PetWindow(runtime);
        chat.Show();
        Click(chat, "MemoryButton");
        var panel = Assert.IsType<MemoryWindow>(Assert.Single(chat.OwnedWindows.Cast<Window>()));
        System.ComponentModel.CancelEventHandler cancel = (_, args) => args.Cancel = true;
        panel.Closing += cancel;
        try
        {
            typeof(PetWindow).GetField("_chatWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pet, chat);
            var closing = new System.ComponentModel.CancelEventArgs();
            typeof(PetWindow).GetMethod("OnClosing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(pet, new object?[] { pet, closing });
            Assert.True(closing.Cancel);
            Assert.False((bool)typeof(PetWindow).GetField("_exitAnimationStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pet)!);
            Assert.True(panel.IsVisible);
        }
        finally { panel.Closing -= cancel; chat.CloseForExit(); }
    });

    [Fact]
    public async Task PausedRuntimeStillUsesSavedMemoryAndSkipsRecordingAcrossRestart()
    {
        using var files = new TestFiles();
        files.Memory.SaveDocument(Original, ChatMemoryStore.EmptyDocument);
        files.Settings.Save(files.Settings.Load() with { MemoryRecordingEnabled = false });
        using var runtime = files.CreateRuntime(async (request, token) =>
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(1024, body.RootElement.GetProperty("max_tokens").GetInt32());
            Assert.Contains(JsonSerializer.Serialize(files.Memory.LoadDocument()), body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
            return Ok("记得。");
        });
        Assert.False(runtime.MemoryRecordingEnabled);
        await runtime.SendAsync("我喜欢什么？", default);
        await runtime.EndConversationAsync();
        Assert.False(File.Exists(files.Memory.PendingPath));
        Assert.Contains("喜欢茶", files.Memory.LoadDocument());
        Assert.Equal(1, files.Requests);
    }

    [Fact]
    public async Task ManualSaveDuringChatDoesNotRecordTheOldInFlightTurn()
    {
        using var files = new TestFiles();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime = files.CreateRuntime(async (_, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return Ok("好的。");
        });
        var send = runtime.SendAsync("旧信息。", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.SaveMemoryDocument(ChatMemoryStore.EmptyDocument, runtime.LoadMemoryDocument());
        release.SetResult();
        await send;
        await runtime.EndConversationAsync();
        Assert.Empty(files.Memory.LoadPending());
        Assert.Equal(1, files.Requests);
        Assert.Equal(3, runtime.Entries.Count);
    }

    [Fact]
    public async Task PauseAndResumeDuringChatDoesNotRecordTheTurnFromBeforeTheToggle()
    {
        using var files = new TestFiles();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var runtime = files.CreateRuntime(async (_, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return Ok("好的。");
        });
        var send = runtime.SendAsync("暂停前发送。", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.SetMemoryRecordingEnabled(false);
        runtime.SetMemoryRecordingEnabled(true);
        release.SetResult();
        await send;
        await runtime.EndConversationAsync();
        Assert.False(File.Exists(files.Memory.PendingPath));
        Assert.Equal(1, files.Requests);
    }

    private static T Control<T>(Window window, string name) where T : class => Assert.IsType<T>(window.FindName(name));
    private static void Click(Window window, string name) => Control<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Memory panel test timed out");
        if (error is not null) throw new TargetInvocationException(error);
    }
    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = text } } } }), Encoding.UTF8, "application/json")
    };
    private sealed class TestFiles : IDisposable
    {
        private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"memory-panel-{Guid.NewGuid():N}");
        public ChatSettingsStore Settings { get; }
        public ChatMemoryStore Memory { get; }
        public int Requests { get; private set; }
        public TestFiles()
        {
            Settings = new ChatSettingsStore(_basePath + ".bin");
            Memory = new ChatMemoryStore(_basePath + ".md");
            Settings.Save(new ChatSettings { ApiKey = "test-only" });
        }
        public ChatRuntime CreateRuntime(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respond = null) =>
            new(new HttpClient(new Handler((request, token) => { Requests++; return respond?.Invoke(request, token) ?? Task.FromResult(Ok("好的。")); })), Settings, Memory);
        public void Dispose()
        {
            File.Delete(_basePath + ".bin");
            File.Delete(Memory.DocumentPath);
            File.Delete(Memory.PendingPath);
            File.Delete(_basePath + ".bin.emotion.json");
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
