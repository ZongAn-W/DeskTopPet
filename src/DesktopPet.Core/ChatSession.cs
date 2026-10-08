namespace DesktopPet.Core;

public sealed class ChatSession(DeepSeekChatClient client)
{
    private readonly List<ChatMessage> _messages = [];
    private readonly object _gate = new();
    private long _generation;
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    public async Task<string> SendAsync(string text, ChatSettings settings, CancellationToken cancellationToken, string? memory = null)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ChatServiceException("请输入想说的话。");
        var user = new ChatMessage("user", text.Trim());
        ChatMessage[] context;
        long generation;
        lock (_gate)
        {
            generation = _generation;
            context = _messages.Append(user).ToArray();
        }
        var reply = await client.ReplyAsync(settings, context, cancellationToken, memory);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (generation != _generation)
                throw new OperationCanceledException(cancellationToken);
            _messages.Add(user);
            _messages.Add(new("assistant", reply));
            if (_messages.Count > 40) _messages.RemoveRange(0, _messages.Count - 40);
        }
        return reply;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _generation++;
            _messages.Clear();
        }
    }
}
