using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class PriorityDataSafetyTests
{
    [Theory]
    [InlineData(".staropt")]
    [InlineData(".json")]
    [InlineData(".starrdb")]
    public async Task TraceDestinationNeverReplacesAnExistingFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "trace-protected-" + Guid.NewGuid().ToString("N") + extension);
        await File.WriteAllTextAsync(path, "original user data");
        try
        {
            using var app = WorkbenchApplication.Create("blank");
            app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
            await Assert.ThrowsAsync<IOException>(() => app.NonSequentialAnalysis.TraceAsync(new(RayDatabasePath: path)));
            Assert.Equal("original user data", await File.ReadAllTextAsync(path));
            Assert.Null(app.NonSequentialAnalysis.GetCurrentSession());
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ClearAndTraceResetsTotalsWhileTraceOnlyAccumulates()
    {
        using var app = WorkbenchApplication.Create("blank");
        app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
        await app.NonSequentialAnalysis.TraceAsync(new());
        var first = app.NonSequentialAnalysis.GetCurrentSession()!;
        await app.NonSequentialAnalysis.TraceAsync(new(Command: NonSequentialTraceCommand.TraceOnly));
        var accumulated = app.NonSequentialAnalysis.GetCurrentSession()!;
        Assert.Equal(2 * first.SourcePowerWatts, accumulated.SourcePowerWatts);
        Assert.Equal(2, accumulated.TracePassCount);
        await app.NonSequentialAnalysis.TraceAsync(new());
        var fresh = app.NonSequentialAnalysis.GetCurrentSession()!;
        Assert.Equal(first.SourcePowerWatts, fresh.SourcePowerWatts);
        Assert.Equal(first.EscapedPowerWatts, fresh.EscapedPowerWatts);
        Assert.Equal(first.BranchCount, fresh.BranchCount);
        Assert.Equal(1, fresh.TracePassCount);
        Assert.NotEqual(accumulated.Id, fresh.Id);
        Assert.Equal(fresh.BranchCount, app.NonSequentialAnalysis.GetRayDatabasePage().TotalBranchCount);
    }

    [Fact]
    public async Task SavingCatalogPreservesUnrelatedJsonAndSelectsOnlyItsActiveFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "catalog-protected-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, CommercialLensCatalogStore.DirectoryName);
        Directory.CreateDirectory(directory);
        var notes = Path.Combine(directory, "engineering-notes.json");
        await File.WriteAllTextAsync(notes, "{\"user\":\"preserve me\"}");
        try
        {
            await CommercialLensCatalogStore.SaveDirectoryAsync(new(1, DateTimeOffset.UtcNow, []), directory);
            Assert.Equal("{\"user\":\"preserve me\"}", await File.ReadAllTextAsync(notes));
            Assert.Empty(CommercialLensCatalogStore.LoadDirectory(root).Entries);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("unrecognized")]
    [InlineData(null)]
    public void InvalidSavedModeFallsBackToSequential(string? mode)
    {
        var settings = new AppSettings { WorkbenchMode = mode! };
        settings.NormalizeDisplaySettings();
        Assert.Equal(nameof(OpticalWorkbenchMode.Sequential), settings.WorkbenchMode);
    }

    [Fact]
    public async Task RetiredCatalogFilesRemainIntactButAreNoLongerActive()
    {
        var root = Path.Combine(Path.GetTempPath(), "catalog-retired-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, CommercialLensCatalogStore.DirectoryName);
        try
        {
            await CommercialLensCatalogStore.SaveDirectoryAsync(new(1, DateTimeOffset.UnixEpoch, [Entry()]), directory);
            var path = Path.Combine(directory, "Thorlabs.json");
            var original = await File.ReadAllBytesAsync(path);
            Assert.Single(CommercialLensCatalogStore.LoadDirectory(root).Entries);
            await CommercialLensCatalogStore.SaveDirectoryAsync(new(1, DateTimeOffset.UtcNow, []), directory);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Empty(CommercialLensCatalogStore.LoadDirectory(root).Entries);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CatalogManufacturerNameDoesNotAuthorizeReplacingAnUnrelatedJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "catalog-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Thorlabs.json");
        await File.WriteAllTextAsync(path, "{\"user\":\"original\"}");
        try
        {
            await Assert.ThrowsAsync<IOException>(() => CommercialLensCatalogStore.SaveDirectoryAsync(new(1, DateTimeOffset.UtcNow, [Entry()]), root));
            Assert.Equal("{\"user\":\"original\"}", await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(root, true); }
    }

    private static CommercialLensEntryDto Entry() => new("test-lens", "Thorlabs", "L1", "Test lens", "", "", "", "singlet", "B", "S",
        1, 50, 25, 20, 45, .1, 400, 700, 0, 1, "metadata-only", null, "test fixture", DateTimeOffset.UnixEpoch, 20);

    [Theory]
    [InlineData(".seq", "SUR 1\nRD 40\nTH -12\nGLA AIR\nSUR 2\nRD -40\nTH 2\nGLA AIR\n")]
    [InlineData(".len", "SRF 1\nRD 40\nTH -12\nGLA AIR\nSRF 2\nRD -40\nTH 2\nGLA AIR\n")]
    public void CommercialImportKeepsSignedThickness(string extension, string text)
    {
        var optic = OpticalFormatCatalog.Import(text, extension);
        Assert.Contains(optic.SurfaceGroup.Items, surface => surface.Thickness == -12);
    }
}
