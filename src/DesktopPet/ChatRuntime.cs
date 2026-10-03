using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using DesktopPet.Core;

namespace DesktopPet;

public sealed class ChatRuntime : IDisposable
{
    private const string AssistantSpeaker = "濂?";
    private const string UserSpeaker = "浣?";
    private const string Greeting = "浣犲ソ鍛€锛屾垜鍦ㄨ繖閲屻€備綘鍙互鍜屾垜鑱婅亰浠婂ぉ鐨勪簨銆?";
    private readonly ChatSettingsStore _settingsStore;
    private readonly object _gate = new();
    private readonly HttpClient _http;
    private CancellationTokenSource? _request;
    private ChatEntry? _pendingUserEntry;
    private bool _disposed;
    private ChatSettings _settings;

    public ChatRuntime(HttpClient http, ChatSettingsStore settingsStore)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        try
        {
            _settings = _settingsStore.Load().Normalize();
        }
        catch (InvalidDataException)
        {
            _settings = new ChatSettings().Normalize();
        }

        Session = new ChatSession(new DeepSeekChatClient(_http));
        Entries = [];
        AddGreeting();
    }

    public ChatSession Session { get; }
    public ChatSettings Settings => _settings;
    public ObservableCollection<ChatEntry> Entries { get; }
    public bool IsBusy
    {
        get
        {
            lock (_gate) return _request is not null;
        }
    }

    public event EventHandler? StateChanged;

    public async Task SendAsync(string text, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(text))
            throw new ChatServiceException("璇疯緭鍏ユ兂璇寸殑璇濄€?");

        var trimmed = text.Trim();
        CancellationTokenSource request;
        ChatEntry userEntry;
        lock (_gate)
        {
            if (_request is not null)
                throw new InvalidOperationException("A chat request is already active.");

            request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _request = request;
            userEntry = new ChatEntry(UserSpeaker, trimmed, "#EEE5F5");
            _pendingUserEntry = userEntry;
            Entries.Add(userEntry);
        }
        NotifyStateChanged();

        try
        {
            var reply = await Session.SendAsync(trimmed, Settings, request.Token).ConfigureAwait(true);
            lock (_gate)
            {
                _pendingUserEntry = null;
                Entries.Add(new ChatEntry(AssistantSpeaker, reply, "#FFFFFF"));
            }
            NotifyStateChanged();
        }
        catch
        {
            lock (_gate)
            {
                if (_pendingUserEntry is not null)
                    Entries.Remove(_pendingUserEntry);
                _pendingUserEntry = null;
            }
            NotifyStateChanged();
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_request, request))
                    _request = null;
            }
            request.Dispose();
            NotifyStateChanged();
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_request is null) return;
            try { _request.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        CancellationTokenSource? request;
        lock (_gate)
        {
            request = _request;
            _request = null;
            _pendingUserEntry = null;
            Session.Clear();
            Entries.Clear();
            AddGreeting();
        }
        try { request?.Cancel(); }
        catch (ObjectDisposedException) { }
        NotifyStateChanged();
    }

    public void SaveSettings(ChatSettings settings)
    {
        ThrowIfDisposed();
        var normalized = (settings ?? throw new ArgumentNullException(nameof(settings))).Normalize();
        _settingsStore.Save(normalized);
        lock (_gate) _settings = normalized;
        NotifyStateChanged();
    }

    public void Dispose()
    {
        CancellationTokenSource? request;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            request = _request;
            _request = null;
            _pendingUserEntry = null;
        }
        try { request?.Cancel(); }
        catch (ObjectDisposedException) { }
        request?.Dispose();
        _http.Dispose();
    }

    private void AddGreeting() => Entries.Add(new ChatEntry(AssistantSpeaker, Greeting, "#FFFFFF"));

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChatRuntime));
    }
}
