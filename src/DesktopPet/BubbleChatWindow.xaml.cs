using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopPet.Core;

namespace DesktopPet;

public interface IBubbleDismissTimer : IDisposable
{
    TimeSpan Interval { get; }
    void Start();
    void Stop();
}

public partial class BubbleChatWindow : Window
{
    private readonly ChatRuntime _runtime;
    private readonly Func<TimeSpan, EventHandler, IBubbleDismissTimer> _timerFactory;
    private readonly ObservableCollection<ChatEntry> _visibleEntries = [];
    private IBubbleDismissTimer? _dismissTimer;
    private bool _closed;
    private bool _exitRequested;
    private bool _replyCompleted;

    public BubbleChatWindow(ChatRuntime runtime)
        : this(runtime, CreateDispatcherTimer) { }

    public BubbleChatWindow(ChatRuntime runtime, Func<TimeSpan, EventHandler, IBubbleDismissTimer> timerFactory)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
        InitializeComponent();
        MessagesList.ItemsSource = _visibleEntries;
        SetStatus(_runtime.SettingsLoadError
            ?? (string.IsNullOrWhiteSpace(_runtime.Settings.ApiKey)
                ? "请在完整聊天窗口中填写 DeepSeek API 密钥。"
                : null));
        _runtime.StateChanged += OnRuntimeStateChanged;
        Deactivated += (_, _) => HandleDeactivated();
        Loaded += (_, _) => { InputBox.Focus(); RefreshEntries(); UpdateBusyState(); };
        Closing += OnClosing;
        Closed += OnClosed;
        RefreshEntries();
    }

    public event EventHandler? ExpandRequested;

    public void SetTailSide(bool onLeft)
    {
        BubbleTail.HorizontalAlignment = onLeft ? System.Windows.HorizontalAlignment.Left : System.Windows.HorizontalAlignment.Right;
        BubbleTail.RenderTransform = new ScaleTransform(onLeft ? 1 : -1, 1, 11, 0);
    }

    private void SetStatus(string? text)
    {
        StatusText.Text = text ?? "";
        StatusText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void CloseForExit()
    {
        _exitRequested = true;
        _runtime.Cancel();
        Close();
    }

    private static IBubbleDismissTimer CreateDispatcherTimer(TimeSpan interval, EventHandler tick) =>
        new DispatcherBubbleDismissTimer(interval, tick);

    private void OnRuntimeStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshEntries();
            UpdateBusyState();
            return;
        }
        _ = Dispatcher.BeginInvoke(() =>
        {
            RefreshEntries();
            UpdateBusyState();
        });
    }

    private void RefreshEntries()
    {
        if (_closed) return;
        var count = _runtime.Settings.BubbleMessageCount;
        var start = Math.Max(0, _runtime.Entries.Count - count);
        _visibleEntries.Clear();
        for (var index = start; index < _runtime.Entries.Count; index++)
            _visibleEntries.Add(_runtime.Entries[index]);
        if (IsVisible) Dispatcher.BeginInvoke(() => ChatScroll.ScrollToEnd());
    }

    private void UpdateBusyState()
    {
        if (_closed) return;
        var busy = _runtime.IsBusy;
        SendButton.Content = busy ? "■" : "↑";
        SendButton.FontSize = busy ? 13 : 22;
        SendButton.ToolTip = busy ? "取消回复" : "发送";
        AutomationProperties.SetName(SendButton, busy ? "取消回复" : "发送");
        NewChatButton.IsEnabled = ExpandButton.IsEnabled = !busy;
        InputBox.IsReadOnly = busy;
        if (busy) StopDismissTimer();
        else if (_runtime.MemoryError is not null) SetStatus(_runtime.MemoryError);
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
        if (_runtime.IsBusy)
        {
            _runtime.Cancel();
            return;
        }
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        if (string.IsNullOrWhiteSpace(_runtime.Settings.ApiKey))
        {
            SetStatus("请在完整聊天窗口中填写 DeepSeek API 密钥。");
            return;
        }

        StopDismissTimer();
        _replyCompleted = false;
        InputBox.Clear();
        SetStatus("她正在想怎么回答你…");
        UpdateBusyState();
        try
        {
            await _runtime.SendAsync(text, CancellationToken.None);
            if (_closed) return;
            _replyCompleted = true;
            SetStatus(_runtime.MemoryError);
            StartDismissTimer(afterReply: true);
        }
        catch (OperationCanceledException)
        {
            if (!_closed)
            {
                SetStatus("已取消，可以修改内容后重新发送。");
                InputBox.Text = text;
            }
        }
        catch (ChatServiceException error)
        {
            if (!_closed) { SetStatus(error.Message); InputBox.Text = text; }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            if (!_closed) { SetStatus("请求未完成，请检查 AI 设置后重试。"); InputBox.Text = text; }
        }
        finally
        {
            if (!_closed)
            {
                UpdateBusyState();
                InputBox.Focus();
            }
        }
    }

    private void OnExpand(object sender, RoutedEventArgs e)
    {
        StopDismissTimer();
        Hide();
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnNewChat(object sender, RoutedEventArgs e)
    {
        StopDismissTimer();
        _runtime.Clear();
        _replyCompleted = false;
        SetStatus(null);
        InputBox.Clear();
        InputBox.Focus();
    }

    private void OnInputFocused(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_closed || _runtime.IsBusy) return;
        switch (_runtime.Settings.BubbleDismiss)
        {
            case BubbleDismissMode.ClickOutside:
                StopDismissTimer();
                break;
            case BubbleDismissMode.ClickOutsideOrIdle:
                StartDismissTimer(afterReply: false);
                break;
            case BubbleDismissMode.AfterReply when _replyCompleted:
                StartDismissTimer(afterReply: false);
                break;
            default:
                StopDismissTimer();
                break;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_closed || _runtime.IsBusy || !IsVisible) return;
        if (_runtime.Settings.BubbleDismiss == BubbleDismissMode.ClickOutsideOrIdle)
            StartDismissTimer(afterReply: false);
        else if (_replyCompleted && _runtime.Settings.BubbleDismiss == BubbleDismissMode.AfterReply && InputBox.IsKeyboardFocusWithin && InputBox.Text.Length > 0)
            StartDismissTimer(afterReply: false);
    }

    private void HandleDeactivated()
    {
        if (_closed || _runtime.IsBusy) return;
        if (_runtime.Settings.BubbleDismiss == BubbleDismissMode.ClickOutside ||
            _runtime.Settings.BubbleDismiss == BubbleDismissMode.ClickOutsideOrIdle)
        {
            StopDismissTimer();
            _ = _runtime.EndConversationAsync();
            Hide();
        }
    }

    private void StartDismissTimer(bool afterReply)
    {
        if (_closed || _runtime.IsBusy) return;
        var mode = _runtime.Settings.BubbleDismiss;
        if (afterReply && mode == BubbleDismissMode.ClickOutside) return;
        var interval = mode == BubbleDismissMode.AfterReply
            ? TimeSpan.FromSeconds(20)
            : TimeSpan.FromSeconds(15);
        StopDismissTimer();
        _dismissTimer = _timerFactory(interval, OnDismissTimerTick);
        _dismissTimer.Start();
    }

    private void OnDismissTimerTick(object? sender, EventArgs e)
    {
        if (_runtime.IsBusy) return;
        StopDismissTimer();
        _ = _runtime.EndConversationAsync();
        Hide();
    }

    private void StopDismissTimer()
    {
        _dismissTimer?.Stop();
        _dismissTimer?.Dispose();
        _dismissTimer = null;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _ = _runtime.EndConversationAsync();
        if (_exitRequested) return;
        e.Cancel = true;
        _runtime.Cancel();
        StopDismissTimer();
        Hide();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        StopDismissTimer();
        _runtime.StateChanged -= OnRuntimeStateChanged;
        _runtime.Cancel();
    }

    private sealed class DispatcherBubbleDismissTimer : IBubbleDismissTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly EventHandler _tick;

        public DispatcherBubbleDismissTimer(TimeSpan interval, EventHandler tick)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _tick = tick;
        }

        public TimeSpan Interval => _timer.Interval;

        public void Start()
        {
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }

        private void OnTick(object? sender, EventArgs e) => _tick(sender, e);
    }
}
