using DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace DocumentAssistant.Infrastructure.Persistence.Configurations;

public class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Content).IsRequired();

        // The domain keeps embeddings as float[]; pgvector needs its own Vector type.
        // EF never passes null into a converter, hence the null-forgiving operators.
        var converter = new ValueConverter<float[]?, Vector?>(
            array => new Vector(array!),
            vector => vector!.ToArray());

        // Arrays are compared by reference by default, so in-place changes would go unnoticed.
        var comparer = new ValueComparer<float[]?>(
            (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
            array => array == null ? 0 : array.Aggregate(0, (hash, value) => HashCode.Combine(hash, value)),
            array => array == null ? null : array.ToArray());

        builder.Property(c => c.Embedding)
            .HasColumnType($"vector({DocumentChunk.EmbeddingDimensions})")
            .HasConversion(converter, comparer);

        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex }).IsUnique();

        // HNSW index for fast approximate nearest-neighbour search by cosine distance.
        builder.HasIndex(c => c.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops");
    }
}
