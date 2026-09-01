using OptilandWorkbench.LensLibraryBuilder;

namespace OptilandWorkbench.Tests;

public sealed class LensLibraryPublisherTests
{
    [Fact]
    public void PublishReplacesManagedContentAndPreservesUnmanagedFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var staging = CreateStagingLibrary(root);
            var output = CreateExistingLibrary(root);

            LensLibraryPublisher.Publish(staging, output);

            Assert.Equal("new-index", File.ReadAllText(Path.Combine(output, "index.json")));
            Assert.Equal("new-project", File.ReadAllText(Path.Combine(output, "projects", "new.staropt")));
            Assert.False(File.Exists(Path.Combine(output, "projects", "old.staropt")));
            Assert.False(Directory.Exists(Path.Combine(output, "catalogs")));
            Assert.Equal(
                "preserved",
                File.ReadAllText(Path.Combine(output, "StockCatalogs", "Thorlabs.json")));
            Assert.Empty(TransactionDirectories(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PublishFailureAfterBackupMoveRestoresCompletePreviousLibrary()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var staging = CreateStagingLibrary(root);
            var output = CreateExistingLibrary(root);

            var error = Assert.Throws<IOException>(() => LensLibraryPublisher.Publish(
                staging,
                output,
                phase =>
                {
                    if (phase == LensLibraryPublishPhase.PreviousLibraryMovedToBackup)
                    {
                        throw new IOException("Injected publish failure.");
                    }
                }));

            Assert.Equal("Injected publish failure.", error.Message);
            Assert.Equal("old-index", File.ReadAllText(Path.Combine(output, "index.json")));
            Assert.Equal("old-project", File.ReadAllText(Path.Combine(output, "projects", "old.staropt")));
            Assert.Equal("legacy", File.ReadAllText(Path.Combine(output, "catalogs", "legacy.txt")));
            Assert.Equal(
                "preserved",
                File.ReadAllText(Path.Combine(output, "StockCatalogs", "Thorlabs.json")));
            Assert.False(File.Exists(Path.Combine(output, "projects", "new.staropt")));
            Assert.Empty(TransactionDirectories(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PublishSkipsReparsePointEntries()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var staging = CreateStagingLibrary(root);
            var output = Path.Combine(root, "library");
            var external = Path.Combine(root, "external");
            Directory.CreateDirectory(external);
            File.WriteAllText(Path.Combine(external, "outside.txt"), "outside");
            var link = Path.Combine(staging, "projects", "linked");
            try
            {
                Directory.CreateSymbolicLink(link, external);
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
            {
                return;
            }

            LensLibraryPublisher.Publish(staging, output);

            Assert.False(File.Exists(Path.Combine(output, "projects", "linked", "outside.txt")));
            Assert.Equal("new-project", File.ReadAllText(Path.Combine(output, "projects", "new.staropt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lens-library-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateStagingLibrary(string root)
    {
        var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(Path.Combine(staging, "projects"));
        File.WriteAllText(Path.Combine(staging, "index.json"), "new-index");
        File.WriteAllText(Path.Combine(staging, "projects", "new.staropt"), "new-project");
        return staging;
    }

    private static string CreateExistingLibrary(string root)
    {
        var output = Path.Combine(root, "library");
        Directory.CreateDirectory(Path.Combine(output, "projects"));
        Directory.CreateDirectory(Path.Combine(output, "catalogs"));
        Directory.CreateDirectory(Path.Combine(output, "StockCatalogs"));
        File.WriteAllText(Path.Combine(output, "index.json"), "old-index");
        File.WriteAllText(Path.Combine(output, "projects", "old.staropt"), "old-project");
        File.WriteAllText(Path.Combine(output, "catalogs", "legacy.txt"), "legacy");
        File.WriteAllText(Path.Combine(output, "StockCatalogs", "Thorlabs.json"), "preserved");
        return output;
    }

    private static string[] TransactionDirectories(string root) => Directory
        .EnumerateDirectories(root)
        .Where(path => Path.GetFileName(path).StartsWith(".library.", StringComparison.Ordinal))
        .ToArray();
}
