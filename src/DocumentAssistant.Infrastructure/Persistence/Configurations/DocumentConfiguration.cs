using DocumentAssistant.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DocumentAssistant.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever(); // Ids are assigned by the domain.

        builder.Property(d => d.FileName).HasMaxLength(Document.MaxFileNameLength).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(Document.MaxContentTypeLength).IsRequired();
        builder.Property(d => d.FailureReason).HasMaxLength(Document.MaxFailureReasonLength);

        // Stored as text so the database stays readable and reordering the enum is safe.
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasMany(d => d.Chunks)
            .WithOne()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // EF reads and writes the private _chunks list directly.
        builder.Navigation(d => d.Chunks).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(d => d.Status);
    }
}
