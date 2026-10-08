using System.Text;
using System.Text.Json;

namespace DesktopPet.Core;

public sealed record PendingMemoryConversation(DateTimeOffset EndedAt, ChatMessage[] Messages);

public sealed class ChatMemoryStore(string path)
{
    public const string EmptyDocument = "# Desktop Pet Memory\n";
    public const int MaxDocumentLength = 12000;
    public string DocumentPath { get; } = Path.GetFullPath(path);
    public string PendingPath => DocumentPath + ".pending.json";

    public string LoadDocument()
    {
        var document = File.Exists(DocumentPath) ? File.ReadAllText(DocumentPath, Encoding.UTF8) : EmptyDocument;
        if (document.Length > MaxDocumentLength)
            throw new InvalidDataException("The memory document is too large.");
        return document;
    }

    public void SaveDocument(string document, string expectedDocument)
    {
        if (string.IsNullOrWhiteSpace(document) || !document.StartsWith("# Desktop Pet Memory", StringComparison.Ordinal) ||
            document.Length > MaxDocumentLength)
            throw new InvalidDataException("The memory summary is not a complete memory document.");
        // A manual edit made while the model is summarizing must not be overwritten.
        if (LoadDocument() != expectedDocument)
            throw new IOException("The memory document changed while it was being summarized.");
        WriteAtomic(DocumentPath, document.Trim() + "\n");
    }

    public List<PendingMemoryConversation> LoadPending()
    {
        if (!File.Exists(PendingPath)) return [];
        try
        {
            var pending = JsonSerializer.Deserialize<List<PendingMemoryConversation>>(File.ReadAllText(PendingPath, Encoding.UTF8))
                ?? throw new JsonException();
            if (pending.Any(item => item is null || item.Messages is null || item.Messages.Length == 0 ||
                item.Messages.Length % 2 != 0 || item.Messages.Where((message, index) => message is null ||
                    message.Role != (index % 2 == 0 ? "user" : "assistant") || string.IsNullOrWhiteSpace(message.Content)).Any()))
                throw new JsonException();
            return pending;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The pending memory conversations cannot be read.", error);
        }
    }

    public void SavePending(IReadOnlyList<PendingMemoryConversation> pending) =>
        WriteAtomic(PendingPath, JsonSerializer.Serialize(pending));

    public void EnsureDocumentExists()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
        using var file = new FileStream(DocumentPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        if (file.Length == 0)
        {
            var bytes = Encoding.UTF8.GetBytes(EmptyDocument);
            file.Write(bytes);
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
