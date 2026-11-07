using System.Text;
using DocumentAssistant.Infrastructure.Storage;

namespace DocumentAssistant.IntegrationTests.Storage;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "docassistant-tests", Guid.NewGuid().ToString());
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests() => _storage = new LocalFileStorage(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task SaveThenOpen_ReturnsSameBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("hello");

        await _storage.SaveAsync("a.pdf", new MemoryStream(bytes));
        await using var stream = await _storage.OpenReadAsync("a.pdf");
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);

        Assert.Equal(bytes, copy.ToArray());
        Assert.False(File.Exists(Path.Combine(_root, "a.pdf.tmp")));
    }

    [Fact]
    public async Task Delete_RemovesFile_AndIgnoresMissingFile()
    {
        await _storage.SaveAsync("a.pdf", new MemoryStream([1, 2, 3]));

        await _storage.DeleteAsync("a.pdf");
        await _storage.DeleteAsync("a.pdf");

        Assert.False(File.Exists(Path.Combine(_root, "a.pdf")));
    }

    [Fact]
    public async Task Open_MissingFile_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _storage.OpenReadAsync("missing.pdf"));
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData(@"..\escape.pdf")]
    [InlineData("sub/nested.pdf")]
    [InlineData(@"C:\Windows\evil.pdf")]
    public async Task PathTraversalKeys_AreRejected(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(key, new MemoryStream([1])));
    }
}
