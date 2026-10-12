using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace DesktopPet.Core;

public sealed record PendingMemoryConversation(DateTimeOffset EndedAt, ChatMessage[] Messages);

public sealed class ChatMemoryStore(string path)
{
    private sealed class DocumentState
    {
        public object Gate { get; } = new();
        public long Revision { get; set; }
        public long ForgetGeneration { get; set; }
    }
    private static readonly ConcurrentDictionary<string, DocumentState> States = new(StringComparer.OrdinalIgnoreCase);
    public const string EmptyDocument = "# Desktop Pet Memory\n";
    public const int MaxDocumentLength = 12000;
    public string DocumentPath { get; } = Path.GetFullPath(path);
    public string PendingPath => DocumentPath + ".pending.json";
    private DocumentState State => States.GetOrAdd(DocumentPath, _ => new());

    public string LoadDocument()
    {
        lock (State.Gate) return LoadDocumentCore();
    }

    private string LoadDocumentCore()
    {
        var document = File.Exists(DocumentPath) ? File.ReadAllText(DocumentPath, Encoding.UTF8) : EmptyDocument;
        if (document.Length > MaxDocumentLength)
            throw new InvalidDataException("The memory document is too large.");
        return document;
    }

    public void SaveDocument(string document, string expectedDocument)
    {
        document = document.Trim() + "\n";
        ValidateDocument(document);
        lock (State.Gate)
        {
            CheckExpectedDocument(expectedDocument);
            WriteAtomic(DocumentPath, document);
            State.Revision++;
        }
    }

    internal MemorySnapshot LoadSnapshot()
    {
        lock (State.Gate) return new(LoadDocumentCore(), State.Revision);
    }

    internal void SaveSummary(string document, MemorySnapshot expected)
    {
        document = document.Trim() + "\n";
        ValidateDocument(document);
        lock (State.Gate)
        {
            if (State.Revision != expected.Revision)
                throw new IOException("The memory document changed while it was being summarized.");
            CheckExpectedDocument(expected.Document);
            WriteAtomic(DocumentPath, document);
            State.Revision++;
        }
    }

    internal void SaveEditedDocument(string document, string expectedDocument)
    {
        ValidateDocument(document);
        lock (State.Gate)
        {
            CheckExpectedDocument(expectedDocument);
            // Forget durable transcripts before updating the document. A later document-write failure
            // must never leave pre-edit transcripts capable of reintroducing deleted information.
            WriteAtomic(PendingPath, "[]");
            State.Revision++;
            State.ForgetGeneration++;
            WriteAtomic(DocumentPath, document);
        }
    }

    internal long ForgetGeneration
    {
        get { lock (State.Gate) return State.ForgetGeneration; }
    }

    private void CheckExpectedDocument(string expectedDocument)
    {
        if (LoadDocumentCore() != expectedDocument)
            throw new IOException("记忆文档已发生变化，请重新加载后再保存。");
    }

    private static void ValidateDocument(string document)
    {
        if (string.IsNullOrWhiteSpace(document) || document.Length > MaxDocumentLength)
            throw new InvalidDataException("The memory summary is not a complete memory document.");
        _ = MemoryDocument.Parse(document);
    }

    public List<PendingMemoryConversation> LoadPending()
    {
        lock (State.Gate) return LoadPendingCore();
    }

    private List<PendingMemoryConversation> LoadPendingCore()
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

    public void SavePending(IReadOnlyList<PendingMemoryConversation> pending)
    {
        lock (State.Gate) WriteAtomic(PendingPath, JsonSerializer.Serialize(pending));
    }

    public void EnsureDocumentExists()
    {
        lock (State.Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
            using var file = new FileStream(DocumentPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
            if (file.Length == 0)
            {
                var bytes = Encoding.UTF8.GetBytes(EmptyDocument);
                file.Write(bytes);
            }
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

internal sealed record MemorySnapshot(string Document, long Revision);
