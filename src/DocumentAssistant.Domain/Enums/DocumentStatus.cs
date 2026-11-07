namespace DocumentAssistant.Domain.Enums;

/// <summary>
/// Lifecycle of an uploaded document as it moves through the ingestion pipeline.
/// </summary>
public enum DocumentStatus
{
    /// <summary>File has been received but not yet processed.</summary>
    Uploaded = 0,

    /// <summary>Text extraction, chunking and embedding are in progress.</summary>
    Processing = 1,

    /// <summary>All chunks are stored and the document can be queried.</summary>
    Ready = 2,

    /// <summary>Processing failed; see <c>Document.FailureReason</c>. Can be retried.</summary>
    Failed = 3
}
