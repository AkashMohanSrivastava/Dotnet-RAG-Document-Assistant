using DocumentAssistant.Application.Abstractions;
using DocumentAssistant.Application.Retrieval;
using DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace DocumentAssistant.Infrastructure.Retrieval;

/// <summary>
/// Search with hand-written SQL: pgvector's &lt;=&gt; operator for vectors and PostgreSQL full-text
/// search for keywords. Raw SQL keeps the queries visible (and easy to explain) and works with the
/// domain's plain float[] embeddings.
/// </summary>
public class PgChunkSearch(DocumentAssistantDbContext context) : IChunkSearch
{
    // Only chunks of Ready documents, optionally of a single document.
    private const string Filter = """
        d.status = 'Ready' AND (@document_id IS NULL OR c.document_id = @document_id)
        """;

    private const string Columns = """
        c.id AS chunk_id, c.document_id, d.file_name, c.chunk_index, c.page_number, c.content
        """;

    public async Task<IReadOnlyList<ChunkMatch>> VectorSearchAsync(
        float[] queryEmbedding, int limit, Guid? documentId, CancellationToken cancellationToken = default)
    {
        // <=> is cosine distance (0 = same direction); similarity = 1 - distance.
        // ORDER BY the bare distance expression so PostgreSQL can use the HNSW index.
        const string sql = $"""
            SELECT {Columns}, 1 - (c.embedding <=> @query) AS score
            FROM document_chunks c
            JOIN documents d ON d.id = c.document_id
            WHERE c.embedding IS NOT NULL AND {Filter}
            ORDER BY c.embedding <=> @query
            LIMIT @limit
            """;

        return await QueryAsync(sql, cancellationToken,
            new NpgsqlParameter("query", new Vector(queryEmbedding)),
            DocumentIdParameter(documentId),
            new NpgsqlParameter("limit", limit));
    }

    public async Task<IReadOnlyList<ChunkMatch>> KeywordSearchAsync(
        string query, int limit, Guid? documentId, CancellationToken cancellationToken = default)
    {
        // plainto_tsquery stems words and drops stop words ("How do I clean filters?" -> 'clean' & 'filter').
        // Swapping & for | means a chunk matches if it has ANY of the words; ts_rank_cd then ranks
        // chunks with more (and closer together) matches higher.
        const string sql = $"""
            WITH q AS (
                SELECT to_tsquery('english', replace(plainto_tsquery('english', @text)::text, ' & ', ' | ')) AS query
            )
            SELECT {Columns}, ts_rank_cd(c.search_vector, q.query) AS score
            FROM document_chunks c
            JOIN documents d ON d.id = c.document_id, q
            WHERE c.search_vector @@ q.query AND {Filter}
            ORDER BY score DESC, c.chunk_index
            LIMIT @limit
            """;

        return await QueryAsync(sql, cancellationToken,
            new NpgsqlParameter("text", query),
            DocumentIdParameter(documentId),
            new NpgsqlParameter("limit", limit));
    }

    private static NpgsqlParameter DocumentIdParameter(Guid? documentId) =>
        new("document_id", NpgsqlDbType.Uuid) { Value = (object?)documentId ?? DBNull.Value };

    private async Task<IReadOnlyList<ChunkMatch>> QueryAsync(
        string sql, CancellationToken cancellationToken, params NpgsqlParameter[] parameters)
    {
        var rows = await context.Database
            .SqlQueryRaw<ChunkMatchRow>(sql, parameters.Cast<object>().ToArray())
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ChunkMatch(r.ChunkId, r.DocumentId, r.FileName, r.ChunkIndex, r.PageNumber, r.Content, r.Score))
            .ToList();
    }

    // EF maps result columns to these properties (snake_case, via the naming convention).
    private sealed class ChunkMatchRow
    {
        public Guid ChunkId { get; init; }
        public Guid DocumentId { get; init; }
        public string FileName { get; init; } = string.Empty;
        public int ChunkIndex { get; init; }
        public int? PageNumber { get; init; }
        public string Content { get; init; } = string.Empty;
        public double Score { get; init; }
    }
}
