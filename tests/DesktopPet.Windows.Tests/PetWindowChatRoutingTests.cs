using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet.Windows.Tests;

public sealed class PetWindowChatRoutingTests
{
    [Fact]
    public void DefaultEntryUsesConfiguredPresentationAndExplicitEntriesSwitchViews()
    {
        RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"desktop-pet-routing-{Guid.NewGuid():N}.bin");
            var store = new ChatSettingsStore(path);
            store.Save(new ChatSettings { ApiKey = "routing-test-key", DefaultPresentation = ChatPresentationMode.FullWindow });
            using var runtime = new ChatRuntime(new HttpClient(new StubHandler()), store);
            var pet = new PetWindow(runtime);
            try
            {
                pet.Show();
                pet.Show();
                pet.OpenDefaultChat();
                var full = GetField<ChatWindow>(pet, "_chatWindow");
                Assert.NotNull(full);
                Assert.True(full!.IsVisible);

                pet.OpenBubbleChat();
                var bubble = GetField<BubbleChatWindow>(pet, "_bubbleWindow");
                Assert.NotNull(bubble);
                Assert.True(bubble!.IsVisible);
                Assert.False(full.IsVisible);
                Assert.Contains(runtime.Entries[^1], ((ItemsControl)bubble.FindName("MessagesList")).Items.Cast<ChatEntry>());

                var bubbleInput = (TextBox)bubble.FindName("InputBox");
                bubbleInput.Text = "from bubble";
                ((Button)bubble.FindName("SendButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => !runtime.IsBusy);
                Assert.Equal("from bubble", runtime.Session.Messages[0].Content);

                pet.OpenFullChat();
                Assert.True(full.IsVisible);
                Assert.False(bubble.IsVisible);
                Assert.Same(runtime.Entries, ((ItemsControl)full.FindName("MessagesList")).ItemsSource);
                var fullInput = (TextBox)full.FindName("InputBox");
                fullInput.Text = "from full";
                ((Button)full.FindName("SendButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => !runtime.IsBusy);
                Assert.Equal(new[] { "from bubble", "reply", "from full", "reply" }, runtime.Session.Messages.Select(message => message.Content));

                pet.OpenBubbleChat();
                Assert.True(bubble.IsVisible);
                Assert.False(full.IsVisible);
                Assert.Contains(((ItemsControl)bubble.FindName("MessagesList")).Items.Cast<ChatEntry>(), entry => entry.Text == "reply");
            }
            finally
            {
                GetField<ChatWindow>(pet, "_chatWindow")?.CloseForExit();
                GetField<BubbleChatWindow>(pet, "_bubbleWindow")?.CloseForExit();
                if (pet.IsVisible) pet.Close();
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PetContextMenuOffersSeparateBubbleAndFullChatCommands()
    {
        RunOnSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"desktop-pet-menu-{Guid.NewGuid():N}.bin");
            var store = new ChatSettingsStore(path);
            using var runtime = new ChatRuntime(new HttpClient(new StubHandler()), store);
            var pet = new PetWindow(runtime);
            var menu = Assert.IsType<ContextMenu>(pet.ContextMenu);
            var headers = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
            Assert.Contains("轻量气泡聊天", headers);
            Assert.Contains("完整聊天窗口", headers);
            File.Delete(path);
        });
    }

    private static T? GetField<T>(object instance, string name) where T : class =>
        (T?)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance);

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "UI integration test timed out");
        if (error is not null) throw new TargetInvocationException(error);
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

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"reply\"}}]}")
            });
    }
}
