namespace DesktopPet.Core;

public sealed class ChatSession(DeepSeekChatClient client)
{
    private readonly List<ChatMessage> _messages = [];
    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    public async Task<string> SendAsync(string text, ChatSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ChatServiceException("请输入想说的话。");
        var user = new ChatMessage("user", text.Trim());
        var reply = await client.ReplyAsync(settings, _messages.Append(user).ToArray(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _messages.Add(user);
        _messages.Add(new("assistant", reply));
        if (_messages.Count > 40) _messages.RemoveRange(0, _messages.Count - 40);
        return reply;
    }

    public void Clear() => _messages.Clear();
}
