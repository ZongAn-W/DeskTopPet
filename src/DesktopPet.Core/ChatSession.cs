namespace DesktopPet.Core;

public sealed class ChatSession(DeepSeekChatClient client, EmotionState? emotion = null, TimeProvider? timeProvider = null)
{
    private readonly List<ChatMessage> _messages = [];
    private readonly object _gate = new();
    private long _generation;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private EmotionState? _emotion = emotion;
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();
    public EmotionState Emotion
    {
        get { lock (_gate) return (_emotion ?? EmotionState.Calm(_clock.GetUtcNow())).At(_clock.GetUtcNow()); }
    }

    public async Task<string> SendAsync(string text, ChatSettings settings, CancellationToken cancellationToken, string? memory = null)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ChatServiceException("请输入想说的话。");
        var user = new ChatMessage("user", text.Trim());
        ChatMessage[] context;
        long generation;
        EmotionState? currentEmotion;
        lock (_gate)
        {
            generation = _generation;
            context = _messages.Append(user).ToArray();
            currentEmotion = _emotion?.At(_clock.GetUtcNow());
        }
        var result = currentEmotion is null
            ? new EmotionalReply(await client.ReplyAsync(settings, context, cancellationToken, memory), Emotion)
            : await client.ReplyWithEmotionAsync(settings, context, currentEmotion, _clock.GetUtcNow(), cancellationToken, memory);
        var reply = result.Text;
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (generation != _generation)
                throw new OperationCanceledException(cancellationToken);
            _messages.Add(user);
            _messages.Add(new("assistant", reply));
            if (currentEmotion is not null) _emotion = result.Emotion;
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
