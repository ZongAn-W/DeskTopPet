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
    private bool _recordingEnabled = true;
    private long _generation;
    private long _forgetGeneration;

    public ChatMemory(DeepSeekChatClient client, ChatMemoryStore store)
    {
        _client = client;
        _store = store;
        _forgetGeneration = store.ForgetGeneration;
    }

    public string DocumentPath => _store.DocumentPath;
    public Exception? LastError { get; private set; }

    public void SetRecordingEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_recordingEnabled == enabled) return;
            _recordingEnabled = enabled;
            _generation++;
        }
    }

    public string LoadDocumentForEditing()
    {
        var document = _store.LoadDocument();
        _ = MemoryDocument.Parse(document);
        return document;
    }

    public void SaveEditedDocument(string document, string expectedDocument)
    {
        lock (_gate)
        {
            try
            {
                _store.SaveEditedDocument(document, expectedDocument);
                LastError = null;
            }
            finally
            {
                SynchronizeForgetGeneration();
            }
        }
    }

    public void RecordTurn(string user, string assistant)
    {
        lock (_gate)
        {
            SynchronizeForgetGeneration();
            if (!_recordingEnabled) return;
            _conversation.Add(new("user", user));
            _conversation.Add(new("assistant", assistant));
        }
    }

    public string ReadDocument()
    {
        try
        {
            var document = _store.LoadDocument();
            return document.Trim() == ChatMemoryStore.EmptyDocument.Trim() ? "" : document;
        }
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
                SynchronizeForgetGeneration();
                if (!_recordingEnabled) return;
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
        long? activeGeneration = null;
        try
        {
            await _summaryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    PendingMemoryConversation batch;
                    MemorySnapshot previous;
                    long generation;
                    lock (_gate)
                    {
                        SynchronizeForgetGeneration();
                        if (!_recordingEnabled) return;
                        LoadPending();
                        if (_pending.Count == 0 || string.IsNullOrWhiteSpace(settings.ApiKey)) return;
                        batch = _pending[0];
                        generation = _generation;
                        activeGeneration = generation;
                        previous = _store.LoadSnapshot();
                    }
                    // Large conversations are summarized in complete-turn chunks, without dropping early facts.
                    var chunk = batch.Messages.Take(40).ToArray();
                    var updated = await _client.SummarizeMemoryAsync(settings, previous.Document, chunk, batch.EndedAt, cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        SynchronizeForgetGeneration();
                        if (!_recordingEnabled || _generation != generation) return;
                        _store.SaveSummary(updated, previous);
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
        catch (Exception error) when (IsMemoryError(error))
        {
            lock (_gate)
            {
                SynchronizeForgetGeneration();
                if (activeGeneration is null || activeGeneration == _generation) LastError = error;
            }
        }
    }

    private void LoadPending()
    {
        if (_loaded) return;
        _pending.AddRange(_store.LoadPending());
        _loaded = true;
    }

    private void SynchronizeForgetGeneration()
    {
        var current = _store.ForgetGeneration;
        if (_forgetGeneration == current) return;
        _forgetGeneration = current;
        _generation++;
        _conversation.Clear();
        _pending.Clear();
        _loaded = false;
    }

    private static bool IsMemoryError(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or ChatServiceException or OperationCanceledException;
}
