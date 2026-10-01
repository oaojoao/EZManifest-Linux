using System.IO;

namespace Windows.Storage;

public sealed class StorageFile
{
    public string Path { get; }

    private StorageFile(string path) => Path = path;

    public static Task<StorageFile> GetFileFromPathAsync(string path) =>
        Task.FromResult(new StorageFile(path));

    public Task<IRandomAccessStream> OpenReadAsync() =>
        Task.FromResult<IRandomAccessStream>(new InMemoryRandomAccessStream(File.ReadAllBytes(Path)));
}

public interface IRandomAccessStream : IDisposable
{
    Stream AsStreamForRead();
}

public sealed class InMemoryRandomAccessStream : IRandomAccessStream
{
    private readonly MemoryStream _stream;

    public InMemoryRandomAccessStream(byte[] data) =>
        _stream = new MemoryStream(data, writable: false);

    public Stream AsStreamForRead() => _stream;

    public void Dispose() => _stream.Dispose();
}
