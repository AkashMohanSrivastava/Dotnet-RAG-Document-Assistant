namespace DocumentAssistant.Application.Abstractions;

/// <summary>
/// Stores raw uploaded files by key. Local disk for now; can be swapped for blob storage later.
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Does nothing if the file does not exist.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
