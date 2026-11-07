using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Application.Retrieval;
using DocumentAssistant.Application.Text;
using DocumentAssistant.Domain.Entities;
using DocumentAssistant.Domain.Enums;
using Microsoft.Extensions.AI;

namespace DocumentAssistant.UnitTests.Fakes;

/// <summary>
/// In-memory repository. Documents become visible only after SaveChangesAsync, like a real DbContext;
/// entities are returned by reference, so changes are "tracked".
/// </summary>
public sealed class FakeDocumentRepository : IDocumentRepository
{
    private readonly List<Document> _pending = [];

    public List<Document> Documents { get; } = [];
    public int SaveCount { get; private set; }
    public bool FailOnSave { get; set; }

    /// <summary>Snapshot of each document's status at every save, to check intermediate states.</summary>
    public List<DocumentStatus> SavedStatuses { get; } = [];

    public void Add(Document document) => _pending.Add(document);

    public Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Documents.FirstOrDefault(d => d.Id == id));

    public Task<Document?> GetByIdWithChunksAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<Document>> GetByStatusAsync(DocumentStatus status, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>(Documents.Where(d => d.Status == status).ToList());

    public Task<IReadOnlyList<Document>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Document>>(Documents.Skip(skip).Take(take).ToList());

    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(Documents.Count);

    public Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DocumentChunk>>(
            Documents.Single(d => d.Id == documentId).Chunks.OrderBy(c => c.ChunkIndex).ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (FailOnSave)
            throw new InvalidOperationException("Simulated database failure.");

        Documents.AddRange(_pending);
        _pending.Clear();
        SaveCount++;
        SavedStatuses.AddRange(Documents.Select(d => d.Status));
        return Task.CompletedTask;
    }
}

public sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Files[key] = buffer.ToArray();
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Files.TryGetValue(key, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException(key);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        Files.Remove(key);
        return Task.CompletedTask;
    }
}

public sealed class FakePdfTextExtractor : IPdfTextExtractor
{
    public ExtractedText Result { get; set; } = new([new ExtractedPage(1, "text")]);
    public Exception? Failure { get; set; }
    public byte[]? LastInput { get; private set; }

    public async Task<ExtractedText> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, cancellationToken);
        LastInput = buffer.ToArray();

        return Failure is null ? Result : throw Failure;
    }
}

/// <summary>
/// Returns a vector per input (first element = input length, so tests can tell them apart) and
/// reports 10 tokens per input. Records every call.
/// </summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public int Dimensions { get; set; } = DocumentChunk.EmbeddingDimensions;
    public bool ReportUsage { get; set; } = true;
    public Exception? Failure { get; set; }
    public List<(IReadOnlyList<string> Inputs, EmbeddingGenerationOptions? Options)> Calls { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        Calls.Add((inputs, options));
        if (Failure is not null)
            throw Failure;

        var embeddings = new GeneratedEmbeddings<Embedding<float>>(inputs.Select(text =>
        {
            var vector = new float[Dimensions];
            vector[0] = text.Length;
            return new Embedding<float>(vector);
        }));
        if (ReportUsage)
            embeddings.Usage = new UsageDetails { InputTokenCount = inputs.Count * 10 };

        return Task.FromResult(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>Returns a fixed reply (with token usage) and records the messages and options it was sent.</summary>
public sealed class FakeChatClient : IChatClient
{
    public string Reply { get; set; } = "An answer [1].";
    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add((messages.ToList(), options));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply))
        {
            ModelId = "fake-model",
            Usage = new UsageDetails { InputTokenCount = 500, OutputTokenCount = 20 }
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>Returns the same matches from both vector and keyword search.</summary>
public sealed class StaticChunkSearch(params ChunkMatch[] matches) : IChunkSearch
{
    public Task<IReadOnlyList<ChunkMatch>> VectorSearchAsync(
        float[] queryEmbedding, int limit, Guid? documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChunkMatch>>(matches.Take(limit).ToList());

    public Task<IReadOnlyList<ChunkMatch>> KeywordSearchAsync(
        string query, int limit, Guid? documentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChunkMatch>>(matches.Take(limit).ToList());
}

public sealed class FakeProcessingQueue : IDocumentProcessingQueue
{
    public List<Guid> Enqueued { get; } = [];

    public ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        Enqueued.Add(documentId);
        return ValueTask.CompletedTask;
    }
}
