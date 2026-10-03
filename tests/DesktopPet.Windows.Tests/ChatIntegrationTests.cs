using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet.Windows.Tests;

public sealed class ChatIntegrationTests
{
    [Fact]
    public void SettingsRoundTripIsEncryptedAndAtomic()
    {
        var path = NewSettingsPath();
        var store = new ChatSettingsStore(path);
        var settings = new ChatSettings { ApiKey = "fake-test-key-never-used-online", Persona = "只用文字回复" };
        try
        {
            store.Save(settings);
            Assert.Equal(settings, store.Load());
            Assert.DoesNotContain(settings.ApiKey, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.DoesNotContain(settings.Persona, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.False(File.Exists(path + ".tmp"));
            store.Save(settings with { Model = "deepseek-v4-pro" });
            Assert.Equal("deepseek-v4-pro", store.Load().Model);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnreadableSettingsGiveRecoveryMessage()
    {
        var path = NewSettingsPath();
        try
        {
            File.WriteAllText(path, "not-encrypted");
            var error = Assert.Throws<InvalidDataException>(() => new ChatSettingsStore(path).Load());
            Assert.Contains("重新填写", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingSettingsStartUnconfigured()
    {
        var settings = new ChatSettingsStore(NewSettingsPath()).Load();
        Assert.Empty(settings.ApiKey);
        Assert.Equal("deepseek-flash", settings.Model);
    }

    [Fact]
    public void SettingsWindowRoundTripsPresentationOptionsAndKeepsSecretsEncrypted()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            var initial = new ChatSettings { ApiKey = "window-test-api-key", Persona = "window-test-persona" };
            store.Save(initial);
            var window = new ChatSettingsWindow(initial with
            {
                DefaultPresentation = ChatPresentationMode.FullWindow,
                BubbleMessageCount = 3,
                BubbleDismiss = BubbleDismissMode.AfterReply
            }, store);
            try
            {
                window.Show();
                Assert.Equal("FullWindow", ((ComboBox)window.FindName("PresentationBox")).SelectedValue);
                Assert.Equal("3", ((ComboBox)window.FindName("MessageCountBox")).SelectedValue);
                Assert.Equal("AfterReply", ((ComboBox)window.FindName("DismissBox")).SelectedValue);
                ((Button)window.FindName("SaveButton"))!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var bytes = File.ReadAllBytes(path);
                Assert.DoesNotContain(initial.ApiKey, Encoding.UTF8.GetString(bytes));
                Assert.DoesNotContain(initial.Persona, Encoding.UTF8.GetString(bytes));
                var saved = store.Load();
                Assert.Equal(ChatPresentationMode.FullWindow, saved.DefaultPresentation);
                Assert.Equal(3, saved.BubbleMessageCount);
                Assert.Equal(BubbleDismissMode.AfterReply, saved.BubbleDismiss);
            }
            finally { window.Close(); File.Delete(path); }
        });
    }

    [Fact]
    public void ChatWindowSendsTextAndDisplaysReply()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new() { ApiKey = "fake-test-key-never-used-online" });
            using var http = new HttpClient(new ResponseHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"choices":[{"message":{"content":"你好，很高兴见到你。"}}]}""", Encoding.UTF8, "application/json")
                })));
            var window = new ChatWindow(http, store);
            try
            {
                var input = (TextBox)window.FindName("InputBox");
                var send = (Button)window.FindName("SendButton");
                input.Text = "你好";
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => !input.IsReadOnly);
                var entries = ((ItemsControl)window.FindName("MessagesList")).Items.Cast<ChatEntry>().ToArray();
                Assert.Equal(3, entries.Length);
                Assert.Equal("你好", entries[1].Text);
                Assert.Equal("你好，很高兴见到你。", entries[2].Text);
                Assert.Empty(input.Text);
                Assert.Equal("发送", send.Content);
                Assert.Null(window.FindName("SpeakBox"));
                ((Button)window.FindName("NewChatButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(((ItemsControl)window.FindName("MessagesList")).Items.Cast<ChatEntry>());
            }
            finally { window.CloseForExit(); File.Delete(path); }
        });
    }

    [Fact]
    public void CancelRestoresDraftAndAllowsRetry()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new() { ApiKey = "fake-test-key-never-used-online" });
            using var http = new HttpClient(new ResponseHandler(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new(HttpStatusCode.OK);
            }));
            var window = new ChatWindow(http, store);
            try
            {
                var input = (TextBox)window.FindName("InputBox");
                var send = (Button)window.FindName("SendButton");
                input.Text = "保留我这句话";
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(input.IsReadOnly);
                Assert.Equal("取消", send.Content);
                Assert.False(((Button)window.FindName("SettingsButton")).IsEnabled);
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => !input.IsReadOnly);
                Assert.Equal("保留我这句话", input.Text);
                Assert.Contains("已取消", ((TextBlock)window.FindName("StatusText")).Text);
                Assert.True(((Button)window.FindName("SettingsButton")).IsEnabled);
            }
            finally { window.CloseForExit(); File.Delete(path); }
        });
    }

    [Fact]
    public void FailedRequestDisplaysErrorAndKeepsDraft()
    {
        RunOnSta(() =>
        {
            var path = NewSettingsPath();
            var store = new ChatSettingsStore(path);
            store.Save(new() { ApiKey = "fake-test-key-never-used-online" });
            using var http = new HttpClient(new ResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.PaymentRequired))));
            var window = new ChatWindow(http, store);
            try
            {
                var input = (TextBox)window.FindName("InputBox");
                input.Text = "今天好吗";
                ((Button)window.FindName("SendButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => !input.IsReadOnly);
                Assert.Equal("今天好吗", input.Text);
                Assert.Contains("余额", ((TextBlock)window.FindName("StatusText")).Text);
            }
            finally { window.CloseForExit(); File.Delete(path); }
        });
    }

    private static string NewSettingsPath() => Path.Combine(Path.GetTempPath(), $"desktop-pet-chat-test-{Guid.NewGuid():N}.bin");

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
}
