namespace DocumentAssistant.Application.Documents;

public class DocumentUploadOptions
{
    public const string SectionName = "DocumentUpload";

    /// <summary>Largest accepted file. Defaults to 20 MB.</summary>
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
}
