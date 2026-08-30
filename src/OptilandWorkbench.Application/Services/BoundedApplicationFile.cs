using CoreBoundedFile = OptilandWorkbench.Core.FileIO.BoundedFile;

namespace OptilandWorkbench.Application.Services;

public static class BoundedApplicationFile
{
    public const long MaximumSettingsBytes = CoreBoundedFile.MaximumSettingsBytes;
    public const long MaximumImageDataBytes = CoreBoundedFile.MaximumImageDataBytes;
    public const long MaximumExportBytes = CoreBoundedFile.MaximumExportBytes;

    public static FileStream OpenRead(string path, long maximumBytes, string description) =>
        CoreBoundedFile.OpenRead(path, maximumBytes, description);

    public static string ReadAllText(string path, long maximumBytes, string description) =>
        CoreBoundedFile.ReadAllText(path, maximumBytes, description);

    public static Task<string> ReadAllTextAsync(
        string path,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default) =>
        CoreBoundedFile.ReadAllTextAsync(path, maximumBytes, description, cancellationToken);

    public static void WriteAllTextAtomic(
        string path,
        string text,
        long maximumBytes,
        string description) =>
        CoreBoundedFile.WriteAllTextAtomic(path, text, maximumBytes, description);

    public static Task WriteAllTextAtomicAsync(
        string path,
        string text,
        long maximumBytes,
        string description,
        CancellationToken cancellationToken = default) =>
        CoreBoundedFile.WriteAllTextAtomicAsync(
            path,
            text,
            maximumBytes,
            description,
            cancellationToken);

    public static void WriteAtomic(
        string path,
        long maximumBytes,
        string description,
        Action<Stream> write) =>
        CoreBoundedFile.WriteAtomic(path, maximumBytes, description, write);
}
