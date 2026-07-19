using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.Tests;

public sealed class WorkspaceSessionTests
{
    [Fact]
    public void PathHashUsesNormalizedCaseInsensitiveAbsolutePath()
    {
        var first = WorkspaceSessionStore.PathHash(@"C:\Optics\Lens.zmx");
        var second = WorkspaceSessionStore.PathHash(@"c:\optics\LENS.ZMX");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public async Task SessionRoundTripsDocumentMetadata()
    {
        var root = TemporaryDirectory();
        try
        {
            var store = new WorkspaceSessionStore(root);
            var instanceId = Guid.NewGuid();
            var session = new WorkspaceSession(
                WorkspaceSessionStore.CurrentVersion,
                "{\"dock\":true}",
                new[]
                {
                    new WorkspaceDocumentDescriptor(
                        "analysis:spot",
                        WorkspaceDocumentKind.Analysis,
                        "点列图",
                        "Spot Diagram",
                        instanceId,
                        new Dictionary<string, string> { ["NumRings"] = "8" },
                        true)
                },
                "analysis:spot");

            await store.SaveAsync(@"C:\Optics\Lens.zmx", session);
            var restored = await store.LoadAsync(@"c:\optics\LENS.ZMX");

            Assert.NotNull(restored);
            Assert.Equal(session.ActiveDocumentId, restored.ActiveDocumentId);
            var document = Assert.Single(restored.Documents);
            Assert.Equal(instanceId, document.InstanceId);
            Assert.True(document.IsLocked);
            Assert.Equal("8", document.Settings!["NumRings"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptSessionIsBackedUpAndIgnored()
    {
        var root = TemporaryDirectory();
        try
        {
            var store = new WorkspaceSessionStore(root);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(store.DefaultLayoutPath, "not-json");

            var restored = await store.LoadAsync(null);

            Assert.Null(restored);
            Assert.False(File.Exists(store.DefaultLayoutPath));
            Assert.Single(Directory.GetFiles(root, "workspace-default.json.invalid-*.bak"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UnsupportedSessionVersionIsBackedUpAndIgnored()
    {
        var root = TemporaryDirectory();
        try
        {
            var store = new WorkspaceSessionStore(root);
            await store.SaveAsync(null, new WorkspaceSession(
                WorkspaceSessionStore.CurrentVersion + 1,
                "{}",
                Array.Empty<WorkspaceDocumentDescriptor>(),
                null));

            var restored = await store.LoadAsync(null);

            Assert.Null(restored);
            Assert.Single(Directory.GetFiles(root, "workspace-default.json.invalid-*.bak"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentSessionSavesDoNotShareTemporaryFiles()
    {
        var root = TemporaryDirectory();
        try
        {
            var store = new WorkspaceSessionStore(root);
            var sessions = Enumerable.Range(0, 16)
                .Select(index => new WorkspaceSession(
                    WorkspaceSessionStore.CurrentVersion,
                    $"{{\"dock\":{index}}}",
                    Array.Empty<WorkspaceDocumentDescriptor>(),
                    $"document:{index}"))
                .ToArray();

            await Task.WhenAll(sessions.Select(session => store.SaveAsync(null, session)));

            var restored = await store.LoadAsync(null);
            Assert.NotNull(restored);
            Assert.Contains(restored!.ActiveDocumentId, sessions.Select(session => session.ActiveDocumentId));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FloatingWindowBoundsAreClampedToWorkingArea()
    {
        var bounds = WorkspaceSessionStore.ClampWindowBounds(
            -5000,
            4000,
            3000,
            100,
            100,
            50,
            1200,
            800);

        Assert.Equal(100, bounds.X);
        Assert.Equal(610, bounds.Y);
        Assert.Equal(1200, bounds.Width);
        Assert.Equal(240, bounds.Height);
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"optiland-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
