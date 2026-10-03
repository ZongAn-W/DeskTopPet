using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using DesktopPet.Core;

namespace DesktopPet;

public partial class ChatWindow : Window
{
    private readonly ObservableCollection<ChatEntry> _entries = [];
    private readonly HttpClient _http;
    private readonly ChatSettingsStore _settingsStore;
    private readonly ChatSession _session;
    private ChatSettings _settings = new();
    private CancellationTokenSource? _request;
    private bool _closed;
    private bool _exitRequested;
    public event EventHandler? ConversationActivity;

    public ChatWindow() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(100) }, new ChatSettingsStore()) { }

    public ChatWindow(HttpClient http, ChatSettingsStore settingsStore)
    {
        InitializeComponent();
        _http = http;
        _settingsStore = settingsStore;
        _session = new(new DeepSeekChatClient(_http));
        MessagesList.ItemsSource = _entries;
        try { _settings = _settingsStore.Load(); }
        catch (InvalidDataException error) { StatusText.Text = error.Message; }
        AddEntry("她", "你好呀，我在这里。你可以和我聊聊今天的事。", false);
        if (string.IsNullOrWhiteSpace(StatusText.Text))
            StatusText.Text = string.IsNullOrWhiteSpace(_settings.ApiKey)
                ? "先点 AI 设置，填写你的 DeepSeek API 密钥。"
                : "Enter 发送，Shift+Enter 换行。";
        Loaded += (_, _) => InputBox.Focus();
        Closing += (_, e) =>
        {
            if (_exitRequested) return;
            e.Cancel = true;
            _request?.Cancel();
            Hide();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _request?.Cancel();
            _http.Dispose();
        };
    }

    public void CloseForExit()
    {
        _exitRequested = true;
        Close();
    }

    private void AddEntry(string speaker, string text, bool user)
    {
        _entries.Add(new(speaker, text, user ? "#EEE5F5" : "#FFFFFF"));
        if (_entries.Count > 100) _entries.RemoveAt(0);
        Dispatcher.BeginInvoke(() => ChatScroll.ScrollToEnd());
    }

    private async void OnSend(object sender, RoutedEventArgs e) => await SendAsync();

    private async void OnInputKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (_request is null) await SendAsync();
        }
    }

    private async Task SendAsync()
    {
        if (_request is not null) { _request.Cancel(); return; }
        var text = InputBox.Text.Trim();
        if (text.Length == 0) return;
        if (string.IsNullOrWhiteSpace(_settings.ApiKey)) { OpenSettings(); if (string.IsNullOrWhiteSpace(_settings.ApiKey)) return; }
        ConversationActivity?.Invoke(this, EventArgs.Empty);
        using var request = new CancellationTokenSource();
        _request = request;
        SendButton.Content = "取消";
        SettingsButton.IsEnabled = NewChatButton.IsEnabled = false;
        InputBox.IsReadOnly = true;
        AddEntry("你", text, true);
        InputBox.Clear();
        StatusText.Text = "她正在想怎么回答你…";
        try
        {
            var reply = await _session.SendAsync(text, _settings, request.Token);
            if (_closed) return;
            AddEntry("她", reply, false);
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
            _request = null;
            if (!_closed)
            {
                SendButton.Content = "发送";
                SettingsButton.IsEnabled = NewChatButton.IsEnabled = true;
                InputBox.IsReadOnly = false;
                InputBox.Focus();
                ConversationActivity?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        var dialog = new ChatSettingsWindow(_settings, _settingsStore) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SavedSettings is null) return;
        _settings = dialog.SavedSettings;
        StatusText.Text = "设置已保存，发送一句话试试吧。";
        InputBox.Focus();
    }

    private void OnNewChat(object sender, RoutedEventArgs e)
    {
        _session.Clear();
        _entries.Clear();
        AddEntry("她", "开始新的聊天吧，我在这里。", false);
        StatusText.Text = "这次对话会从这里开始。";
    }

}
