using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

public sealed class FullReliabilityServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "full-reliability-" + Guid.NewGuid().ToString("N"));
    public FullReliabilityServiceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public async Task MaterialModelChangeMakesAnExistingSessionStale()
    {
        using var app = WorkbenchApplication.Create("blank");
        app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
        await app.NonSequentialAnalysis.TraceAsync(new());
        Assert.False(app.NonSequentialAnalysis.GetCurrentSession()!.IsStale);
        var workspace = (WorkspaceCoordinator)typeof(WorkbenchApplication).GetField("_workspace", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        workspace.Runtime.CurrentOptic.Materials.Register(new ConstantIndexMaterial("Air", 1.01));
        Assert.True(app.NonSequentialAnalysis.GetCurrentSession()!.IsStale);
        await Assert.ThrowsAsync<InvalidOperationException>(() => app.NonSequentialAnalysis.TraceAsync(new(Command: NonSequentialTraceCommand.TraceOnly)));
    }

    [Fact]
    public async Task DetectorViewerRejectsCurrentGeometryAgainstAnOldDatabase()
    {
        using var app = WorkbenchApplication.Create("blank");
        app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
        var detectorId = app.NonSequential.AddObject(NonSequentialObjectKind.DetectorRectangle);
        var row = app.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
        app.NonSequential.UpdateObject(new(detectorId, row.Enabled, row.Visible, row.Kind, row.Name,
            row.ReferenceObjectId, row.ContainingObjectId, 0, 0, 5, 0, 0, 0, new DetectorRectangleParameters(2, 2, 2, 2, true, true)));
        await app.NonSequentialAnalysis.TraceAsync(new());
        Assert.NotNull(app.NonSequentialAnalysis.GetDetectorView(new(detectorId)));
        app.NonSequential.UpdateObject(new(detectorId, row.Enabled, row.Visible, row.Kind, row.Name,
            row.ReferenceObjectId, row.ContainingObjectId, 0, 0, 6, 0, 0, 0, new DetectorRectangleParameters(4, 4, 2, 2, true, true)));
        Assert.Throws<InvalidOperationException>(() => app.NonSequentialAnalysis.GetDetectorView(new(detectorId)));
    }

    [Theory]
    [InlineData(NonSequentialTraceOutputMode.InMemory)]
    [InlineData(NonSequentialTraceOutputMode.LayoutSample)]
    [InlineData(NonSequentialTraceOutputMode.SummaryOnly)]
    public async Task WorkbenchDoesNotSilentlySubstituteDatabaseOutput(NonSequentialTraceOutputMode mode)
    {
        using var app = WorkbenchApplication.Create("blank");
        app.NonSequential.AddObject(NonSequentialObjectKind.SourceRay);
        await Assert.ThrowsAsync<NotSupportedException>(() => app.NonSequentialAnalysis.TraceAsync(new(OutputMode: mode)));
        Assert.Null(app.NonSequentialAnalysis.GetCurrentSession());
    }

    [Fact]
    public async Task DisposingWhileTraceIsPendingDoesNotDisposeItsReleaseGate()
    {
        var app = WorkbenchApplication.Create("blank");
        var service = (NonSequentialAnalysisService)app.NonSequentialAnalysis;
        var gate = (SemaphoreSlim)typeof(NonSequentialAnalysisService).GetField("_analysisTraceGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
        await gate.WaitAsync();
        var pending = service.TraceAsync(new());
        service.Dispose();
        gate.Release();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await gate.WaitAsync(); gate.Release();
        app.Dispose();
    }

    [Theory]
    [InlineData("materials")]
    [InlineData("layers")]
    [InlineData("bands")]
    public void CoatingExperimentRejectsNullArrayElements(string field)
    {
        var experiment = Examples.Create(DesignKind.Antireflection);
        experiment = field switch
        {
            "materials" => experiment with { Materials = [null!] },
            "layers" => experiment with { Layers = [null!] },
            _ => experiment with { Target = experiment.Target with { StopBands = [null!] } }
        };
        Assert.Throws<InvalidDataException>(() => experiment.Validate());
    }

    [Fact]
    public void LegalJsonNullSettingsRestoreUsableDefaults()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{\"LayoutSlots\":null,\"AnalysisSettings\":null,\"ReportExport\":null}");
        var settings = AppSettings.Load(path);
        Assert.NotNull(settings.LayoutSlots);
        Assert.NotNull(settings.AnalysisSettings);
        Assert.NotNull(settings.ReportExport);
        Assert.Null(settings.LoadWarning);
    }

    [Fact]
    public async Task InvalidUtf8FailsBothBoundedReadersInsteadOfReplacingCharacters()
    {
        var path = Path.Combine(_root, "invalid.json");
        await File.WriteAllBytesAsync(path, [123, 34, 120, 34, 58, 34, 0xC3, 0x28, 34, 125]);
        Assert.Throws<InvalidDataException>(() => BoundedFile.ReadAllText(path, 4096, "test"));
        await Assert.ThrowsAsync<InvalidDataException>(() => BoundedFile.ReadAllTextAsync(path, 4096, "test"));
        Assert.Equal(10, new FileInfo(path).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-1)]
    public void InvalidLayoutSlotNeverAliasesAnotherSlot(int slot)
    {
        var store = new WorkspaceSessionStore(_root);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SlotPath(slot));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings().LoadLayoutSlot(slot));
    }

    [Fact]
    public async Task RecoveryScanFindsNewestCopyBeyondTheFormerThousandDirectoryCutoff()
    {
        var recovery = Path.Combine(_root, "Recovery");
        using var app = WorkbenchApplication.Create("blank");
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[0] with { Label = "unsaved" });
        await using (var store = new DocumentRecoveryStore(app.Documents, recovery))
        { Assert.True(await store.CheckpointAsync()); await store.StopAsync(false); }
        var originalSession = Assert.Single(Directory.EnumerateDirectories(recovery));
        Directory.Move(originalSession, Path.Combine(recovery, new string('f', 32)));
        for (var index = 0; index < 1001; index++) Directory.CreateDirectory(Path.Combine(recovery, index.ToString("x32")));
        await using var observer = new DocumentRecoveryStore(app.Documents, recovery);
        Assert.Equal("unsaved", app.Prescription.GetSurfaces()[0].Label);
        Assert.Equal(new string('f', 32), Path.GetFileName(Assert.Single(observer.ListCandidates()).SessionDirectory));
    }
}
