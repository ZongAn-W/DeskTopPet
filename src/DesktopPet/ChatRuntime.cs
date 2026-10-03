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
    private RequestState? _request;
    private bool _disposed;
    private bool _httpDisposed;
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
        RequestState request;
        lock (_gate)
        {
            if (_request is not null)
                throw new InvalidOperationException("A chat request is already active.");

            request = new RequestState(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _request = request;
            request.Entry = new ChatEntry(UserSpeaker, trimmed, "#EEE5F5") { Role = "user" };
            Entries.Add(request.Entry);
        }
        NotifyStateChanged();

        try
        {
            var reply = await Session.SendAsync(trimmed, Settings, request.Source.Token).ConfigureAwait(true);
            lock (_gate)
            {
                if (ReferenceEquals(_request, request) && !request.Invalidated && !_disposed)
                {
                    Entries.Add(new ChatEntry(AssistantSpeaker, reply, "#FFFFFF") { Role = "assistant" });
                }
            }
            NotifyStateChanged();
        }
        catch
        {
            lock (_gate)
            {
                Entries.Remove(request.Entry);
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
            request.Source.Dispose();
            DisposeHttpIfNeeded();
            NotifyStateChanged();
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_request is null) return;
            try { _request.Source.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();
        RequestState? request;
        lock (_gate)
        {
            request = _request;
            if (request is not null)
                request.Invalidated = true;
            Session.Clear();
            Entries.Clear();
            AddGreeting();
        }
        try { request?.Source.Cancel(); }
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
        RequestState? request;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            request = _request;
            if (request is not null)
                request.Invalidated = true;
        }
        try { request?.Source.Cancel(); }
        catch (ObjectDisposedException) { }
        DisposeHttpIfNeeded();
    }

    private void AddGreeting() => Entries.Add(new ChatEntry(AssistantSpeaker, Greeting, "#FFFFFF") { Role = "assistant" });

    private sealed class RequestState(CancellationTokenSource source)
    {
        public CancellationTokenSource Source { get; } = source;
        public ChatEntry Entry { get; set; } = null!;
        public bool Invalidated { get; set; }
    }

    private void DisposeHttpIfNeeded()
    {
        lock (_gate)
        {
            if (!_disposed || _request is not null || _httpDisposed) return;
            _httpDisposed = true;
        }
        _http.Dispose();
    }

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChatRuntime));
    }
}
