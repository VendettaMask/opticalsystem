using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

public sealed class ProjectArchiveTests
{
    [Fact]
    public async Task UnsavedSnapshotDeduplicatesAndReopensWithoutOriginalInputs()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        using var app = WorkbenchApplication.Create("cooke");
        var surface = app.Prescription.GetSurfaces()[1];
        app.Prescription.UpdateSurface(surface with { Radius = surface.Radius + 1 });
        var before = app.Documents.GetSnapshot();
        var first = files.Path("original-one.txt"); var second = files.Path("original-two.txt");
        await File.WriteAllTextAsync(first, "波长,data\n550,1"); File.Copy(first, second);
        app.ProjectArchives.LinkFile(first, required: true); app.ProjectArchives.LinkFile(second, required: true);
        using var capture = await app.ProjectArchives.CaptureAsync();
        var archive = files.Path("工程 & backup.starpack");
        var info = await app.ProjectArchives.CreateAsync(archive, capture, new HashSet<string>());
        Assert.Equal(3, info.Entries.Count);
        using (var zip = ZipFile.OpenRead(archive)) Assert.Equal(3, zip.Entries.Count); // Project, one shared payload, manifest.
        Assert.Equal(before.Revision, app.Documents.GetSnapshot().Revision);
        Assert.Equal(before.IsDirty, app.Documents.GetSnapshot().IsDirty); Assert.Null(app.Documents.CurrentPath);
        var inputBytes = File.ReadAllBytes(first); File.Delete(first); File.Delete(second);
        using var destination = WorkbenchApplication.Create();
        var restored = await destination.ProjectArchives.RestoreAsync(archive, files.Path("恢复 & folder"));
        Assert.Equal(surface.Radius + 1, destination.Prescription.GetSurfaces()[1].Radius);
        Assert.All(restored.Files, file => Assert.Equal(inputBytes, File.ReadAllBytes(file.SourcePath)));
        using var reopened = WorkbenchApplication.Create();
        await reopened.Documents.OpenAsync(restored.ProjectPath);
        Assert.Equal(2, reopened.ProjectArchives.LinkedFiles.Count);
        using var secondCapture = await reopened.ProjectArchives.CaptureAsync();
        await reopened.ProjectArchives.CreateAsync(files.Path("again.starpack"), secondCapture, new HashSet<string>());
    }

    [Fact]
    public async Task NonSequentialDatabaseIsPinnedAcrossSessionReplacementAndRestored()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        using var app = WorkbenchApplication.Create();
        app.Modes.SwitchTo(Dto.OpticalWorkbenchMode.NonSequential);
        app.NonSequential.AddObject(Dto.NonSequentialObjectKind.SourceRay);
        var initial = await app.NonSequentialAnalysis.TraceAsync(new(Dto.NonSequentialTraceOutputMode.RayDatabase));
        using var capture = await app.ProjectArchives.CaptureAsync();
        var original = File.ReadAllBytes(initial.RayDatabasePath!);
        await app.NonSequentialAnalysis.TraceAsync(new(Dto.NonSequentialTraceOutputMode.RayDatabase));
        Assert.False(File.Exists(initial.RayDatabasePath));
        var target = files.Path("nsc.starpack");
        await app.ProjectArchives.CreateAsync(target, capture, capture.Files.Select(file => file.RelativePath).ToHashSet());
        using var destination = WorkbenchApplication.Create();
        var restored = await destination.ProjectArchives.RestoreAsync(target, files.Path("nsc-restored"));
        Assert.Equal(Dto.OpticalWorkbenchMode.NonSequential, destination.Modes.CurrentMode);
        var database = Assert.Single(restored.Files, file => file.Kind == "ray-database");
        Assert.Equal(original, File.ReadAllBytes(database.SourcePath));
        Assert.Equal(database.SourcePath, destination.NonSequentialAnalysis.GetCurrentSession()!.RayDatabasePath);
        var newer = await destination.NonSequentialAnalysis.TraceAsync(new(Dto.NonSequentialTraceOutputMode.RayDatabase));
        using var newerCapture = await destination.ProjectArchives.CaptureAsync();
        Assert.Equal(newer.RayDatabasePath, Assert.Single(newerCapture.Files, file => file.Kind == "ray-database").SourcePath);
        var newerArchive = files.Path("newer.starpack");
        await destination.ProjectArchives.CreateAsync(newerArchive, newerCapture, newerCapture.Files.Select(file => file.RelativePath).ToHashSet());
        var newerRestored = await ProjectArchiveStore.RestoreAsync(newerArchive, files.Path("newer-restored"));
        Assert.Equal(File.ReadAllBytes(newer.RayDatabasePath!), File.ReadAllBytes(Path.Combine(newerRestored.Directory, "results", "rays.starrdb")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/rooted.txt")]
    [InlineData("C:/escape.txt")]
    [InlineData("inputs/CON.txt")]
    [InlineData("inputs/file. ")]
    public async Task NonPortablePathsAreRejectedBeforePublishing(string relative)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var native = await Native(files);
        var archive = files.Path("old.starpack"); await File.WriteAllTextAsync(archive, "old bytes");
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectArchiveStore.CreateAsync(archive, "Test", "1.0", 0, "Sequential",
            [new("main.staropt", "project", native), new(relative, "attachment", native)]));
        Assert.Equal("old bytes", File.ReadAllText(archive));
    }

    [Theory]
    [InlineData("collect")]
    [InlineData("pack")]
    [InlineData("restore")]
    public async Task CancellationPreservesTheDestinationAndRemovesStaging(string phase)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var native = await Native(files); var archive = files.Path("cancel.starpack");
        var sources = new ProjectArchiveSource[] { new("main.staropt", "project", native) };
        using var cancellation = new CancellationTokenSource();
        var progress = new Observer(value => { if (value.Phase == phase) cancellation.Cancel(); });
        if (phase == "restore")
        {
            await ProjectArchiveStore.CreateAsync(archive, "Test", "1.0", 0, "Sequential", sources);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectArchiveStore.RestoreAsync(archive, files.Path("restore"), progress, cancellation.Token));
            Assert.False(Directory.Exists(files.Path("restore")));
        }
        else
        {
            await File.WriteAllTextAsync(archive, "previous archive");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectArchiveStore.CreateAsync(archive, "Test", "1.0", 0, "Sequential", sources, progress, cancellation.Token));
            Assert.Equal("previous archive", File.ReadAllText(archive));
        }
        Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(archive)!, ".starpack-*"));
    }

    [Fact]
    public async Task TamperingIsDetectedBeforeTheCurrentDocumentChanges()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        using var source = WorkbenchApplication.Create("cooke");
        using var capture = await source.ProjectArchives.CaptureAsync();
        var archive = files.Path("bad.starpack");
        await source.ProjectArchives.CreateAsync(archive, capture, new HashSet<string>());
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            var entry = zip.Entries.First(item => item.FullName.StartsWith("data/"));
            var bytes = new byte[entry.Length]; using var stream = entry.Open(); stream.ReadExactly(bytes);
            bytes[^1] ^= 1; stream.Position = 0; stream.Write(bytes);
        }
        using var destination = WorkbenchApplication.Create("tessar");
        var before = destination.Documents.GetSnapshot();
        await Assert.ThrowsAsync<InvalidDataException>(() => destination.ProjectArchives.RestoreAsync(archive, files.Path("invalid")));
        Assert.Equal(before, destination.Documents.GetSnapshot()); Assert.False(Directory.Exists(files.Path("invalid")));
    }

    [Fact]
    public async Task MissingFilesLimitsAndExistingFoldersDoNotProducePartialArchives()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var native = await Native(files);
        var sources = new ProjectArchiveSource[] { new("main.staropt", "project", native) };
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectArchiveStore.CreateAsync(files.Path("limit.starpack"), "Test", "1.0", 0,
            "Sequential", sources, limits: new(1, 1)));
        Assert.False(File.Exists(files.Path("limit.starpack")));
        await Assert.ThrowsAsync<FileNotFoundException>(() => ProjectArchiveStore.CreateAsync(files.Path("missing.starpack"), "Test", "1.0", 0,
            "Sequential", [sources[0], new("inputs/missing.txt", "input", files.Path("missing.txt"))]));
        var archive = files.Path("valid.starpack"); await ProjectArchiveStore.CreateAsync(archive, "Test", "1.0", 0, "Sequential", sources);
        var existing = files.Path("existing"); Directory.CreateDirectory(existing); await File.WriteAllTextAsync(Path.Combine(existing, "keep.txt"), "keep");
        await Assert.ThrowsAsync<IOException>(() => ProjectArchiveStore.RestoreAsync(archive, existing));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "keep.txt")));
    }

    [Fact]
    public async Task StreamingAtomicWriterAcceptsLargeBudgetsAndKeepsExistingOutputOnFailure()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var path = files.Path("streamed.starpack");
        await OpticalSystemDesign.Core.FileIO.BoundedFile.WriteStreamingAtomicAsync(path, 1L << 33, "Archive",
            async (stream, token) => await stream.WriteAsync(new byte[] { 1, 2, 3 }, token));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => OpticalSystemDesign.Core.FileIO.BoundedFile.WriteStreamingAtomicAsync(path, 2, "Archive",
            async (stream, token) => await stream.WriteAsync(new byte[] { 4, 5, 6 }, token)));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".*.tmp"));
    }

    [Fact]
    public async Task Zip64DirectoryOffsetsBeyondFourGiBAreReadableWithoutLargeAllocations()
    {
        using var stream = new SparseStream(); stream.Position = (1L << 32) + 4096;
        byte[] payload = [1, 2, 3]; var hash = Convert.ToHexStringLower(SHA256.HashData(payload));
        var manifest = new ProjectArchiveManifest(ProjectArchiveStore.Format, 1, "ZIP64", "1.0", DateTimeOffset.UtcNow, 0,
            "Sequential", [new("main.staropt", "project", payload.Length, hash, true)]);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var body = zip.CreateEntry("data/" + hash).Open()) body.Write(payload);
            using var header = zip.CreateEntry("manifest.json").Open(); await JsonSerializer.SerializeAsync(header, manifest);
        }
        var restored = ProjectArchiveStore.Inspect(stream);
        Assert.Equal("ZIP64", restored.Name); Assert.True(stream.Length > uint.MaxValue);
        Assert.True(stream.AllocatedBytes < 64 * 1024);
    }

    private static async Task<string> Native(CameraSensorFileTests.TemporaryFiles files)
    {
        var path = files.Path("source.staropt");
        await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()], 0), path); return path;
    }

    [Fact]
    public async Task ArchiveExportCannotOverwriteTheWorkingNativeProject()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); using var app = WorkbenchApplication.Create();
        var native = files.Path("working.staropt"); await app.Documents.SaveAsync(native);
        var bytes = File.ReadAllBytes(native);
        using var capture = await app.ProjectArchives.CaptureAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => app.ProjectArchives.CreateAsync(native, capture, new HashSet<string>()));
        Assert.Equal(bytes, File.ReadAllBytes(native));
    }

    [Fact]
    public async Task AllConfigurationsAndTheirSnapshotsSurviveRestoration()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var document = new StarOptProjectDocument([Optic.CreateBlank(), Optic.CreateCookeTriplet()], 1);
        var native = files.Path("configurations.staropt"); await StarOptProjectStore.SaveAsync(document, native);
        var before = await StarOptProjectStore.LoadAsync(native);
        var archive = files.Path("configurations.starpack");
        await ProjectArchiveStore.CreateAsync(archive, "Configurations", "1.0", 7, "Sequential", [new("main.staropt", "project", native)]);
        var restored = await ProjectArchiveStore.RestoreAsync(archive, files.Path("all-configurations"));
        var after = await StarOptProjectStore.LoadAsync(restored.ProjectPath);
        Assert.Equal(1, after.ActiveConfigurationIndex); Assert.Equal(2, after.Configurations.Count);
        var json = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        Assert.Equal(before.Configurations.Select(optic => JsonSerializer.Serialize(optic.ToSnapshot(), json)),
            after.Configurations.Select(optic => JsonSerializer.Serialize(optic.ToSnapshot(), json)));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("duplicate-name")]
    [InlineData("missing-member")]
    public async Task ManifestAndMemberInconsistenciesAreRejected(string mutation)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles(); var native = await Native(files);
        var archive = files.Path("metadata.starpack");
        var manifest = await ProjectArchiveStore.CreateAsync(archive, "Metadata", "1.0", 0, "Sequential", [new("main.staropt", "project", native)]);
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            if (mutation == "missing-member") zip.Entries.First(entry => entry.FullName.StartsWith("data/")).Delete();
            else
            {
                manifest = mutation == "version" ? manifest with { Version = 99 }
                    : manifest with { Entries = [.. manifest.Entries, manifest.Entries[0] with { RelativePath = "MAIN.staropt" }] };
                zip.GetEntry("manifest.json")!.Delete();
                using var header = zip.CreateEntry("manifest.json").Open(); await JsonSerializer.SerializeAsync(header, manifest);
            }
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectArchiveStore.InspectAsync(archive));
    }
    private sealed class Observer(Action<ProjectArchiveProgress> action) : IProgress<ProjectArchiveProgress>
    { public void Report(ProjectArchiveProgress value) => action(value); }
    private sealed class SparseStream : Stream
    {
        private readonly SortedDictionary<long, byte> _bytes = [];
        private long _length;
        internal int AllocatedBytes => _bytes.Count;
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => true;
        public override long Length => _length; public override long Position { get; set; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            count = (int)Math.Min(count, Math.Max(0, _length - Position));
            for (var i = 0; i < count; i++) buffer[offset + i] = _bytes.GetValueOrDefault(Position++);
            return count;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++) _bytes[Position++] = buffer[offset + i];
            _length = Math.Max(_length, Position);
        }
        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        { SeekOrigin.Begin => offset, SeekOrigin.Current => Position + offset, _ => Length + offset };
        public override void SetLength(long value) => _length = value;
    }
}
