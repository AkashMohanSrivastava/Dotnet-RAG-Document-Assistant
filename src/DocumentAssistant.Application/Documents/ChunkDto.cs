using DocumentAssistant.Domain.Entities;

namespace DocumentAssistant.Application.Documents;

public record ChunkDto(Guid Id, int ChunkIndex, int? PageNumber, string Content, bool HasEmbedding)
{
    public static ChunkDto FromEntity(DocumentChunk chunk) =>
        new(chunk.Id, chunk.ChunkIndex, chunk.PageNumber, chunk.Content, chunk.HasEmbedding);
}
