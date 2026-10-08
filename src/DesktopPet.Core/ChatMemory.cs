namespace DesktopPet.Core;

public sealed class ChatMemory
{
    private readonly DeepSeekChatClient _client;
    private readonly ChatMemoryStore _store;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _summaryGate = new(1, 1);
    private readonly List<ChatMessage> _conversation = [];
    private readonly List<PendingMemoryConversation> _pending = [];
    private bool _loaded;

    public ChatMemory(DeepSeekChatClient client, ChatMemoryStore store)
    {
        _client = client;
        _store = store;
    }

    public string DocumentPath => _store.DocumentPath;
    public Exception? LastError { get; private set; }

    public void RecordTurn(string user, string assistant)
    {
        lock (_gate)
        {
            _conversation.Add(new("user", user));
            _conversation.Add(new("assistant", assistant));
        }
    }

    public string ReadDocument()
    {
        try { return _store.LoadDocument(); }
        catch (Exception error) when (IsMemoryError(error))
        {
            LastError = error;
            return "";
        }
    }

    public void EnsureDocumentExists() => _store.EnsureDocumentExists();

    public async Task EndConversationAsync(ChatSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate)
            {
                LoadPending();
                if (_conversation.Count > 0)
                {
                    var batch = new PendingMemoryConversation(DateTimeOffset.Now, _conversation.ToArray());
                    _store.SavePending(_pending.Append(batch).ToArray());
                    _pending.Add(batch);
                    _conversation.Clear();
                }
            }
            await RetryPendingAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (IsMemoryError(error)) { LastError = error; }
    }

    public async Task RetryPendingAsync(ChatSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            await _summaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    PendingMemoryConversation batch;
                    lock (_gate)
                    {
                        LoadPending();
                        if (_pending.Count == 0 || string.IsNullOrWhiteSpace(settings.ApiKey)) return;
                        batch = _pending[0];
                    }
                    var previous = _store.LoadDocument();
                    // Large conversations are summarized in complete-turn chunks, without dropping early facts.
                    var chunk = batch.Messages.Take(40).ToArray();
                    var updated = await _client.SummarizeMemoryAsync(settings, previous, chunk, batch.EndedAt, cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        _store.SaveDocument(updated, previous);
                        var remaining = _pending.Skip(1).ToList();
                        if (chunk.Length < batch.Messages.Length)
                            remaining.Insert(0, batch with { Messages = batch.Messages.Skip(chunk.Length).ToArray() });
                        _store.SavePending(remaining);
                        _pending.Clear();
                        _pending.AddRange(remaining);
                        LastError = null;
                    }
                }
            }
            finally { _summaryGate.Release(); }
        }
        catch (Exception error) when (IsMemoryError(error)) { LastError = error; }
    }

    private void LoadPending()
    {
        if (_loaded) return;
        _pending.AddRange(_store.LoadPending());
        _loaded = true;
    }

    private static bool IsMemoryError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ChatServiceException or OperationCanceledException;
}
