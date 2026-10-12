using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;

namespace OptilandWorkbench.Tests;

public sealed class DocumentRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "document-recovery-" + Guid.NewGuid().ToString("N"));
    public DocumentRecoveryTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task RecoveryDoesNotSaveTheDocumentOrOverwriteTheOriginal()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var original = Path.Combine(_root, "original.staropt");
        await app.Documents.SaveAsync(original);
        var bytes = await File.ReadAllBytesAsync(original);
        Edit(app, 81);
        var source = app.Documents.GetRecoveryState();
        await using var store = new DocumentRecoveryStore(app.Documents, Path.Combine(_root, "recovery"));
        Assert.True(await store.CheckpointAsync());
        Assert.Equal(source, app.Documents.GetRecoveryState());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
        await Assert.ThrowsAsync<ArgumentException>(() => app.Documents.WriteRecoveryCopyAsync(original));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
        // Simulate process death by releasing the owner without clean-exit retirement.
        await store.StopAsync(cleanExit: false);
        using var recovered = WorkbenchApplication.Create();
        await using var next = new DocumentRecoveryStore(recovered.Documents, Path.Combine(_root, "recovery"));
        var candidate = Assert.Single(next.ListCandidates());
        await next.RestoreAsync(candidate);
        Assert.True(recovered.Documents.GetRecoveryState().IsDirty);
        Assert.Null(recovered.Documents.CurrentPath);
        Assert.Equal(81, recovered.Prescription.GetSurfaces()[1].Radius);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
        await next.StopAsync(cleanExit: true);
    }

    [Fact]
    public async Task ActiveInstancesAreExcludedAndOnlyTwoLatestCheckpointsRemain()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var root = Path.Combine(_root, "recovery");
        await using var store = new DocumentRecoveryStore(app.Documents, root);
        using var observer = WorkbenchApplication.Create();
        await using var other = new DocumentRecoveryStore(observer.Documents, root);
        for (var value = 80; value < 84; value++) { Edit(app, value); Assert.True(await store.CheckpointAsync()); }
        Assert.False(await store.CheckpointAsync());
        Assert.Empty(other.ListCandidates());
        await store.StopAsync(cleanExit: false);
        var candidates = other.ListCandidates();
        Assert.Equal(2, candidates.Count);
        Assert.True(candidates[0].Entry.Revision > candidates[1].Entry.Revision);
        foreach (var candidate in candidates) other.DeleteCandidate(candidate);
        Assert.Empty(other.ListCandidates());
    }

    [Fact]
    public async Task SavedOrExplicitlyReplacedDocumentsRetireTheirOwnRecoveryCopies()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var root = Path.Combine(_root, "recovery");
        await using var store = new DocumentRecoveryStore(app.Documents, root);
        Edit(app, 80); await store.CheckpointAsync();
        await app.Documents.SaveAsync(Path.Combine(_root, "saved.staropt"));
        Assert.False(await store.CheckpointAsync());
        Assert.Empty(Directory.GetFiles(root, "*.json", SearchOption.AllDirectories));
        Edit(app, 82); await store.CheckpointAsync();
        var oldGeneration = app.Documents.GetRecoveryState().DocumentGeneration;
        app.Documents.NewBlank(); await store.CheckpointAsync();
        foreach (var metadata in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            Assert.NotEqual(oldGeneration, JsonSerializer.Deserialize<DocumentRecoveryEntry>(await File.ReadAllTextAsync(metadata))!.DocumentGeneration);
        await store.StopAsync(cleanExit: true);
    }

    [Fact]
    public async Task CorruptOrIncompleteSnapshotsAreNotOfferedAndRemainOnDisk()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var root = Path.Combine(_root, "recovery");
        await using var store = new DocumentRecoveryStore(app.Documents, root);
        Edit(app, 80); await store.CheckpointAsync(); await store.StopAsync(false);
        var project = Assert.Single(Directory.GetFiles(root, "*.staropt", SearchOption.AllDirectories));
        await File.WriteAllTextAsync(project, "incomplete checkpoint");
        using var observer = WorkbenchApplication.Create();
        await using var other = new DocumentRecoveryStore(observer.Documents, root);
        Assert.Empty(other.ListCandidates());
        Assert.True(File.Exists(project));
        var before = observer.Documents.GetRecoveryState();
        await Assert.ThrowsAnyAsync<Exception>(() => observer.Documents.RestoreRecoveryCopyAsync(project));
        Assert.Equal(before, observer.Documents.GetRecoveryState());
    }

    [Fact]
    public async Task RecoveryPreservesMultipleConfigurationsAndTheirActiveSelection()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.MultiConfiguration.Add();
        app.MultiConfiguration.Activate(1);
        Edit(app, 83);
        var root = Path.Combine(_root, "recovery");
        await using var store = new DocumentRecoveryStore(app.Documents, root);
        Assert.True(await store.CheckpointAsync()); await store.StopAsync(false);
        using var observer = WorkbenchApplication.Create();
        await using var other = new DocumentRecoveryStore(observer.Documents, root);
        await other.RestoreAsync(Assert.Single(other.ListCandidates()));
        Assert.Equal(2, observer.MultiConfiguration.GetRows().Count);
        Assert.Equal(83, observer.Prescription.GetSurfaces()[1].Radius);
        Assert.True(observer.Documents.GetRecoveryState().IsDirty);
        await other.StopAsync(true);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task EverySupportedHistoricalPayloadOpensAndResavesWithoutChangingOriginalBytes(int version)
    {
        var options = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            FormatVersion = version,
            Product = "Optiland Workbench",
            ActiveConfigurationIndex = 0,
            Configurations = new[] { Optic.CreateBlank().ToSnapshot() }
        }, options);
        using var output = new MemoryStream();
        using (var compressed = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true)) compressed.Write(payload);
        var compressedBytes = output.ToArray();
        var bytes = new byte[52 + compressedBytes.Length];
        "STAROPT\x1a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), compressedBytes.Length);
        SHA256.HashData(payload).CopyTo(bytes, 20); compressedBytes.CopyTo(bytes, 52);
        var original = Path.Combine(_root, $"legacy-v{version}.staropt");
        await File.WriteAllBytesAsync(original, bytes);
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(original);
        var native = Path.Combine(_root, $"migrated-v{version}.staropt");
        await app.Documents.SaveAsync(native);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(original));
        await app.Documents.OpenAsync(native);
        Assert.Equal(2, app.Prescription.GetSurfaces().Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task HistoricalJsonSnapshotSchemasMigrateWithoutRewritingTheirSource(int schema)
    {
        var snapshot = Optic.CreateBlank().ToSnapshot() with { SchemaVersion = schema };
        var options = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, options);
        var path = Path.Combine(_root, $"old-schema-{schema}.optic.json");
        await File.WriteAllBytesAsync(path, bytes);
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(path);
        var migrated = Path.Combine(_root, $"old-schema-{schema}.staropt");
        await app.Documents.SaveAsync(migrated);
        await app.Documents.OpenAsync(migrated);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(2, app.Prescription.GetSurfaces().Count);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("System")]
    public void LegacySettingsLoadWithoutRewritingAndKeepSupportedPreferences(string theme)
    {
        var path = Path.Combine(_root, "old-settings.json");
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{{\"Theme\":\"{theme}\",\"DecimalPlaces\":5,\"LeftPaneWidth\":400}}");
        File.WriteAllBytes(path, bytes);
        var loaded = AppSettings.Load(path);
        Assert.Null(loaded.LoadWarning);
        Assert.Equal(theme, loaded.Theme);
        Assert.Equal(5, loaded.DecimalPlaces);
        Assert.InRange(loaded.LeftPaneWidth, 240, 280);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    private static void Edit(WorkbenchApplication app, double radius)
        => app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with { Radius = radius });
}
