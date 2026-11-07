using DocumentAssistant.Application.Abstractions;

namespace DocumentAssistant.Infrastructure.Storage;

/// <summary>
/// Stores files in a folder on local disk. Keys are file names inside that folder.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;

    public LocalFileStorage(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        Directory.CreateDirectory(_rootPath);
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);

        // Write to a temp file and move it into place, so a failed upload never leaves a half-written file.
        var tempPath = path + ".tmp";
        try
        {
            await using (var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(file, cancellationToken);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path))
            throw new FileNotFoundException($"No stored file with key '{key}'.", key);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        File.Delete(ResolvePath(key)); // no-op if missing
        return Task.CompletedTask;
    }

    /// <summary>Maps a key to a path and rejects anything that would escape the root folder.</summary>
    private string ResolvePath(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = Path.GetFullPath(Path.Combine(_rootPath, key));
        if (Path.GetDirectoryName(path) != _rootPath)
            throw new ArgumentException($"Invalid storage key '{key}'.", nameof(key));

        return path;
    }
}
