namespace OptilandWorkbench.LensLibraryBuilder;

internal enum LensLibraryPublishPhase
{
    ReplacementPrepared,
    PreviousLibraryMovedToBackup
}

internal static class LensLibraryPublisher
{
    private static readonly HashSet<string> ManagedEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "catalogs",
        "projects",
        "index.json"
    };

    public static void Publish(
        string stagingDirectory,
        string outputDirectory,
        Action<LensLibraryPublishPhase>? checkpoint = null)
    {
        var fullStagingDirectory = Path.GetFullPath(stagingDirectory);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(fullStagingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Lens-library staging directory was not found: {fullStagingDirectory}");
        }

        if (!File.Exists(Path.Combine(fullStagingDirectory, "index.json")))
        {
            throw new InvalidDataException("Lens-library staging output does not contain index.json.");
        }

        var outputParent = Directory.GetParent(Path.TrimEndingDirectorySeparator(fullOutputDirectory))
            ?? throw new InvalidOperationException("Lens-library output directory must have a parent directory.");
        Directory.CreateDirectory(outputParent.FullName);
        var outputName = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullOutputDirectory));
        var transactionId = Guid.NewGuid().ToString("N");
        var replacementDirectory = Path.Combine(
            outputParent.FullName,
            $".{outputName}.publish-{transactionId}");
        var backupDirectory = Path.Combine(
            outputParent.FullName,
            $".{outputName}.backup-{transactionId}");
        var committed = false;

        try
        {
            Directory.CreateDirectory(replacementDirectory);
            if (Directory.Exists(fullOutputDirectory))
            {
                CopyUnmanagedEntries(fullOutputDirectory, replacementDirectory);
            }

            CopyDirectory(fullStagingDirectory, replacementDirectory);
            checkpoint?.Invoke(LensLibraryPublishPhase.ReplacementPrepared);

            if (Directory.Exists(fullOutputDirectory))
            {
                Directory.Move(fullOutputDirectory, backupDirectory);
                try
                {
                    checkpoint?.Invoke(LensLibraryPublishPhase.PreviousLibraryMovedToBackup);
                    Directory.Move(replacementDirectory, fullOutputDirectory);
                    committed = true;
                }
                catch (Exception publishException)
                {
                    try
                    {
                        if (!Directory.Exists(fullOutputDirectory) && Directory.Exists(backupDirectory))
                        {
                            Directory.Move(backupDirectory, fullOutputDirectory);
                        }
                    }
                    catch (Exception rollbackException)
                    {
                        throw new IOException(
                            $"Lens-library publish failed and rollback could not restore '{fullOutputDirectory}'. " +
                            $"The previous library remains at '{backupDirectory}'.",
                            new AggregateException(publishException, rollbackException));
                    }

                    throw;
                }
            }
            else
            {
                Directory.Move(replacementDirectory, fullOutputDirectory);
                committed = true;
            }
        }
        finally
        {
            TryDeleteDirectory(replacementDirectory);
            if (committed)
            {
                TryDeleteDirectory(backupDirectory);
            }
        }
    }

    private static void CopyUnmanagedEntries(string sourceDirectory, string destinationDirectory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDirectory))
        {
            if (ManagedEntries.Contains(Path.GetFileName(entry)))
            {
                continue;
            }

            var destination = Path.Combine(destinationDirectory, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                CopyDirectory(entry, destination);
            }
            else
            {
                File.Copy(entry, destination, overwrite: true);
            }
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                destinationDirectory,
                Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var destination = Path.Combine(
                destinationDirectory,
                Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The published or restored library is already authoritative. A stale transaction
            // directory is recoverable and must not turn a successful publish into a failure.
        }
    }
}
