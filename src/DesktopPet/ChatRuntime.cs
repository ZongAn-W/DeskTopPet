using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using DesktopPet.Core;

namespace DesktopPet;

public sealed class ChatRuntime : IDisposable
{
    private const string AssistantSpeaker = "她";
    private const string UserSpeaker = "你";
    private const string Greeting = "你好呀，我在这里。你可以和我聊聊今天的事。";
    private readonly ChatSettingsStore _settingsStore;
    private readonly object _gate = new();
    private readonly HttpClient _http;
    private readonly ChatMemory _memory;
    private readonly CancellationTokenSource _memoryStop = new();
    private Task _memoryWork = Task.CompletedTask;
    private RequestState? _request;
    private bool _disposed;
    private bool _httpDisposed;
    private ChatSettings _settings;

    public ChatRuntime(HttpClient http, ChatSettingsStore settingsStore, ChatMemoryStore? memoryStore = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        try
        {
            _settings = _settingsStore.Load().Normalize();
        }
        catch (InvalidDataException error)
        {
            _settings = new ChatSettings().Normalize();
            SettingsLoadError = error.Message;
        }

        var client = new DeepSeekChatClient(_http);
        Session = new ChatSession(client);
        _memory = new ChatMemory(client, memoryStore ?? new ChatMemoryStore(settingsStore.MemoryPath));
        Entries = [];
        AddGreeting();
        _memoryWork = RetryMemoryAsync();
    }

    public ChatSession Session { get; }
    internal ChatSettingsStore SettingsStore => _settingsStore;
    public ChatSettings Settings => _settings;
    public string? SettingsLoadError { get; }
    public string MemoryPath => _memory.DocumentPath;
    public string? MemoryError => _memory.LastError is null ? null : "记忆暂未更新，待整理内容已保留。下次关闭聊天时会重试，请检查网络和文件权限。";
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
            await _memoryWork.WaitAsync(request.Source.Token).ConfigureAwait(true);
            var reply = await Session.SendAsync(trimmed, Settings, request.Source.Token, _memory.ReadDocument()).ConfigureAwait(true);
            lock (_gate)
            {
                if (ReferenceEquals(_request, request) && !request.Invalidated && !_disposed)
                {
                    Entries.Add(new ChatEntry(AssistantSpeaker, reply, "#FFFFFF") { Role = "assistant" });
                    _memory.RecordTurn(trimmed, reply);
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
        _ = EndConversationAsync();
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
        _memoryWork = RetryMemoryAsync();
        NotifyStateChanged();
    }

    public Task EndConversationAsync()
    {
        if (_disposed) return Task.CompletedTask;
        _memoryWork = SaveMemoryAsync();
        return _memoryWork;
    }

    public void EnsureMemoryDocumentExists() => _memory.EnsureDocumentExists();

    private async Task SaveMemoryAsync()
    {
        await _memory.EndConversationAsync(Settings, _memoryStop.Token).ConfigureAwait(false);
        if (!_disposed) NotifyStateChanged();
    }

    private async Task RetryMemoryAsync()
    {
        await _memory.RetryPendingAsync(Settings, _memoryStop.Token).ConfigureAwait(false);
        if (!_disposed) NotifyStateChanged();
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
            Session.Clear();
        }
        try { request?.Source.Cancel(); }
        catch (ObjectDisposedException) { }
        _memoryStop.Cancel();
        _ = _memoryWork.ContinueWith(_ => DisposeHttpIfNeeded(), TaskScheduler.Default);
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
            if (!_disposed || _request is not null || !_memoryWork.IsCompleted || _httpDisposed) return;
            _httpDisposed = true;
        }
        _http.Dispose();
        _memoryStop.Dispose();
    }

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ChatRuntime));
    }
}
