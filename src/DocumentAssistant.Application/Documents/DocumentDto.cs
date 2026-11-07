using DocumentAssistant.Domain.Entities;

namespace DocumentAssistant.Application.Documents;

public record DocumentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Status,
    string? FailureReason,
    DateTimeOffset UploadedAt,
    DateTimeOffset? ProcessedAt)
{
    public static DocumentDto FromEntity(Document document) => new(
        document.Id,
        document.FileName,
        document.ContentType,
        document.FileSizeBytes,
        document.Status.ToString(),
        document.FailureReason,
        document.UploadedAt,
        document.ProcessedAt);
}
