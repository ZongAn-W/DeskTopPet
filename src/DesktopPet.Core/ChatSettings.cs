namespace DesktopPet.Core;

public sealed record ChatSettings
{
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "deepseek-flash";
    public string Persona { get; init; } = "你是住在用户桌面上的AI女孩，用自然、温柔的中文陪用户聊天。默认回答简短。可以表达关心和幽默；诚实说明你是AI，不声称看见或听见未提供的信息。";
}

public sealed record ChatMessage(string Role, string Content);

public sealed class ChatServiceException(string message) : Exception(message);
