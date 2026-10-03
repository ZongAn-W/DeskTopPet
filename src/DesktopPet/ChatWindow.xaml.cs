using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using DesktopPet.Core;

namespace DesktopPet;

public partial class ChatWindow : Window
{
    private readonly ChatRuntime _runtime;
    private readonly bool _ownsRuntime;
    private bool _closed;
    private bool _exitRequested;

    public event EventHandler? ConversationActivity;

    public ChatWindow()
        : this(new ChatRuntime(new HttpClient { Timeout = TimeSpan.FromSeconds(100) }, new ChatSettingsStore()), true) { }

    public ChatWindow(HttpClient http, ChatSettingsStore settingsStore)
        : this(new ChatRuntime(http, settingsStore), true) { }

    public ChatWindow(ChatRuntime runtime)
        : this(runtime, false) { }

    private ChatWindow(ChatRuntime runtime, bool ownsRuntime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _ownsRuntime = ownsRuntime;
        InitializeComponent();
        MessagesList.ItemsSource = _runtime.Entries;
        StatusText.Text = _runtime.SettingsLoadError
            ?? (string.IsNullOrWhiteSpace(_runtime.Settings.ApiKey)
                ? "先点 AI 设置，填写你的 DeepSeek API 密钥。"
                : "Enter 发送，Shift+Enter 换行。");
        _runtime.StateChanged += OnRuntimeStateChanged;
        Loaded += (_, _) => { InputBox.Focus(); UpdateBusyState(); };
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public void CloseForExit() { _exitRequested = true; Close(); }

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) { UpdateBusyState(); return; }
        _ = Dispatcher.BeginInvoke(UpdateBusyState);
    }

    private void UpdateBusyState()
    {
        if (_closed) return;
        var busy = _runtime.IsBusy;
        SendButton.Content = busy ? "取消" : "发送";
        SettingsButton.IsEnabled = NewChatButton.IsEnabled = !busy;
        InputBox.IsReadOnly = busy;
        if (!busy) Dispatcher.BeginInvoke(() => ChatScroll.ScrollToEnd());
        ConversationActivity?.Invoke(this, EventArgs.Empty);
    }

    private async void OnSend(object sender, RoutedEventArgs e) => await SendAsync();

    private async void OnInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (!_runtime.IsBusy) await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (_runtime.IsBusy) { _runtime.Cancel(); return; }
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        if (string.IsNullOrWhiteSpace(_runtime.Settings.ApiKey))
        {
            OpenSettings();
            if (string.IsNullOrWhiteSpace(_runtime.Settings.ApiKey)) return;
        }
        ConversationActivity?.Invoke(this, EventArgs.Empty);
        InputBox.Clear();
        StatusText.Text = "她正在想怎么回答你…";
        UpdateBusyState();
        try
        {
            await _runtime.SendAsync(text, CancellationToken.None);
            if (_closed) return;
            StatusText.Text = "Enter 发送，Shift+Enter 换行。";
        }
        catch (OperationCanceledException)
        {
            if (!_closed) { StatusText.Text = "已取消，可以修改内容后重新发送。"; InputBox.Text = text; }
        }
        catch (ChatServiceException error)
        {
            if (!_closed) { StatusText.Text = error.Message; InputBox.Text = text; }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            if (!_closed) { StatusText.Text = "请求未完成，请检查 AI 设置后重试。"; InputBox.Text = text; }
        }
        finally
        {
            if (!_closed) { UpdateBusyState(); InputBox.Focus(); ConversationActivity?.Invoke(this, EventArgs.Empty); }
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        var dialog = new ChatSettingsWindow(_runtime.Settings, _runtime.SettingsStore) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SavedSettings is null) return;
        _runtime.SaveSettings(dialog.SavedSettings);
        StatusText.Text = "设置已保存，发送一句话试试吧。";
        InputBox.Focus();
    }

    private void OnNewChat(object sender, RoutedEventArgs e)
    {
        _runtime.Clear();
        StatusText.Text = "这次对话会从这里开始。";
        InputBox.Clear();
        InputBox.Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested) return;
        e.Cancel = true;
        _runtime.Cancel();
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        _runtime.StateChanged -= OnRuntimeStateChanged;
        _runtime.Cancel();
        if (_ownsRuntime) _runtime.Dispose();
    }
}
