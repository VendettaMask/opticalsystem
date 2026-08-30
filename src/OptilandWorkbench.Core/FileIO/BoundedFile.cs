using System.Text;

namespace OptilandWorkbench.Core.FileIO;

public static class BoundedFile
{
    public const long MaximumOpticalDocumentBytes = 64L * 1024 * 1024;
    public const long MaximumSettingsBytes = 4L * 1024 * 1024;
    public const long MaximumCatalogBytes = 64L * 1024 * 1024;
    public const long MaximumExpandedCatalogBytes = 128L * 1024 * 1024;
    public const long MaximumImageDataBytes = 64L * 1024 * 1024;
    public const long MaximumExportBytes = 256L * 1024 * 1024;

    public static FileStream OpenRead(string path, long maximumBytes, string description)
    {
        if (maximumBytes <= 0 || maximumBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes)
        {
            stream.Dispose();
            throw new InvalidDataException($"{description} exceeds the {maximumBytes:N0}-byte input limit.");
        }

        return stream;
    }

    public static async Task<byte[]> ReadAllBytesAsync(
        string path,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default)
    {
        await using var stream = OpenRead(path, maximumBytes, description);
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    public static byte[] ReadAllBytes(string path, long maximumBytes, string description)
    {
        using var stream = OpenRead(path, maximumBytes, description);
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public static async Task WriteAllTextAtomicAsync(
        string path,
        string text,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateOutputLength(Encoding.UTF8.GetByteCount(text), maximumBytes, description);
        await WriteAllBytesAtomicAsync(
            path,
            Encoding.UTF8.GetBytes(text),
            maximumBytes,
            description,
            cancellationToken).ConfigureAwait(false);
    }

    public static void WriteAllTextAtomic(
        string path,
        string text,
        long maximumBytes,
        string description)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateOutputLength(Encoding.UTF8.GetByteCount(text), maximumBytes, description);
        WriteAllBytesAtomic(path, Encoding.UTF8.GetBytes(text), maximumBytes, description);
    }

    public static async Task WriteAllBytesAtomicAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default)
    {
        ValidateOutputLength(bytes.Length, maximumBytes, description);
        var (fullPath, temporaryPath) = PrepareAtomicWrite(path);
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    public static void WriteAllBytesAtomic(
        string path,
        ReadOnlySpan<byte> bytes,
        long maximumBytes,
        string description)
    {
        ValidateOutputLength(bytes.Length, maximumBytes, description);
        var (fullPath, temporaryPath) = PrepareAtomicWrite(path);
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    public static async Task WriteAtomicAsync(
        string path,
        long maximumBytes,
        string description,
        Func<Stream, CancellationToken, Task> writeAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writeAsync);
        ValidateMaximumBytes(maximumBytes);
        var (fullPath, temporaryPath) = PrepareAtomicWrite(path);
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var bounded = new MaximumLengthWriteStream(stream, maximumBytes, description);
                await writeAsync(bounded, cancellationToken).ConfigureAwait(false);
                await bounded.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    public static void WriteAtomic(
        string path,
        long maximumBytes,
        string description,
        Action<Stream> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ValidateMaximumBytes(maximumBytes);
        var (fullPath, temporaryPath) = PrepareAtomicWrite(path);
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                var bounded = new MaximumLengthWriteStream(stream, maximumBytes, description);
                write(bounded);
                bounded.Flush();
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    public static async Task<string> ReadAllTextAsync(
        string path,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default)
    {
        await using var stream = OpenRead(path, maximumBytes, description);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string ReadAllText(string path, long maximumBytes, string description)
    {
        using var stream = OpenRead(path, maximumBytes, description);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);
        return reader.ReadToEnd();
    }

    public static byte[] ReadToEnd(Stream stream, long maximumBytes, string description)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maximumBytes <= 0 || maximumBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"{description} exceeds the {maximumBytes:N0}-byte expanded-data limit.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static void ValidateOutputLength(long length, long maximumBytes, string description)
    {
        ValidateMaximumBytes(maximumBytes);
        if (length > maximumBytes)
        {
            throw new InvalidDataException($"{description} exceeds the {maximumBytes:N0}-byte output limit.");
        }
    }

    private static void ValidateMaximumBytes(long maximumBytes)
    {
        if (maximumBytes <= 0 || maximumBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }
    }

    private static (string FullPath, string TemporaryPath) PrepareAtomicWrite(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The output path does not have a parent directory.");
        Directory.CreateDirectory(directory);
        return (fullPath, Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp"));
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class MaximumLengthWriteStream(
        Stream inner,
        long maximumBytes,
        string description) : Stream
    {
        private long _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            inner.Write(buffer, offset, count);
            _written += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            inner.Write(buffer);
            _written += buffer.Length;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            _written += buffer.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
        }

        private void EnsureCapacity(int count)
        {
            if (count < 0 || _written > maximumBytes - count)
            {
                throw new InvalidDataException(
                    $"{description} exceeds the {maximumBytes:N0}-byte output limit.");
            }
        }
    }
}
