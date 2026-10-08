using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OptilandWorkbench.App;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class AuditReliabilityFixTests
{
    [Fact]
    public async Task OpenWaitingForCommitCannotOverwriteANewDocument()
    {
        using var context = new RecordingContext(Optic.CreateCookeTriplet());
        using var workspace = new WorkspaceCoordinator(context);
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var old = Optic.CreateTessarLens();
        var documents = new OpticalDocumentService(workspace, (_, _) =>
        {
            context.OpenThread = Thread.CurrentThread;
            ready.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return Task.FromResult(new LoadedOpticalDocument(old, [old], 0));
        }, (_, _, _) => Task.CompletedTask);
        var pending = Task.Run(() => documents.OpenAsync("old-pending.staropt"));
        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            lock (context.RawGate)
            {
                release.Set();
                Assert.True(context.CommitAttempt.Wait(TimeSpan.FromSeconds(5)));
                documents.NewBlank();
            }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal("Untitled optic", documents.GetSnapshot().Name);
            Assert.Null(documents.CurrentPath);
            Assert.False(documents.GetSnapshot().CanUndo);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task EditDuringOpenIsPreservedAndCancelsThePendingReplacement()
    {
        using var workspace = new WorkspaceCoordinator(new OpticContext(Optic.CreateCookeTriplet()));
        var read = new TaskCompletionSource<LoadedOpticalDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        var documents = new OpticalDocumentService(workspace, (_, _) => read.Task, (_, _, _) => Task.CompletedTask);
        var prescription = new PrescriptionService(workspace);
        var pending = documents.OpenAsync("pending.staropt");
        var surface = prescription.GetSurfaces()[1];
        prescription.UpdateSurface(surface with { Radius = surface.Radius + 2 });
        var expected = SerializeDocument(workspace.Runtime.CaptureDocument());
        var old = Optic.CreateTessarLens();
        read.SetResult(new(old, [old], 0));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(expected, SerializeDocument(workspace.Runtime.CaptureDocument()));
        Assert.True(documents.GetSnapshot().IsDirty);
    }

    [Theory]
    [InlineData(".zmx")]
    [InlineData(".seq")]
    [InlineData(".len")]
    [InlineData(".json")]
    public async Task MultiConfigurationSaveRejectsLossyFormatWithoutOverwritingOrClearingDirty(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lossy-save-{Guid.NewGuid():N}{extension}");
        try
        {
            using var app = WorkbenchApplication.Create("cooke");
            app.MultiConfiguration.Add();
            await File.WriteAllTextAsync(path, "existing file");
            var before = app.Documents.GetSnapshot();
            await Assert.ThrowsAsync<NotSupportedException>(() => app.Documents.SaveAsync(path));
            Assert.Equal("existing file", await File.ReadAllTextAsync(path));
            Assert.Equal(before, app.Documents.GetSnapshot());
            Assert.Equal(2, app.MultiConfiguration.GetRows().Count);
            Assert.True(app.Documents.GetSnapshot().IsDirty);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualUiSaveAllowsContinuationOnlyWhenTheCurrentRevisionWasSaved(bool editDuringSave)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var workspace = (WorkspaceCoordinator)typeof(WorkbenchApplication)
            .GetField("_workspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        double writtenRadius = 0;
        var documents = new OpticalDocumentService(workspace, async (document, _, token) =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
            writtenRadius = document.ActiveOptic.SurfaceGroup.Items[1].Radius;
        });
        typeof(WorkbenchApplication).GetField("<Documents>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(app, documents);
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        typeof(MainWindow).GetField("_application", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, app);
        var surface = app.Prescription.GetSurfaces()[1];
        app.Prescription.UpdateSurface(surface with { Radius = surface.Radius + 1 });
        var before = app.Prescription.GetSurfaces()[1].Radius;
        var guard = UnsavedChangesGuard.CanContinueAsync(true,
            () => Task.FromResult(UnsavedChangesChoice.Save),
            () => window.TrySaveProjectToPathAsync("controlled-save.staropt"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (editDuringSave)
        {
            surface = app.Prescription.GetSurfaces()[1];
            app.Prescription.UpdateSurface(surface with { Radius = surface.Radius + 2 });
        }
        release.SetResult();
        Assert.Equal(!editDuringSave, await guard);
        Assert.Equal(editDuringSave, documents.GetSnapshot().IsDirty);
        Assert.Equal(before, writtenRadius);
        Assert.Equal(before + (editDuringSave ? 2 : 0), app.Prescription.GetSurfaces()[1].Radius);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("even")]
    [InlineData("odd")]
    [InlineData("forbes")]
    [InlineData("grating")]
    public void NestedGeometryChangesSynchronizeThePrescriptionAndDetachCachedTraces(string kind)
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Geometry = kind switch
        {
            "even" => new EvenAsphereGeometry(surface.Radius, 0, [0]),
            "odd" => new OddAsphereGeometry(surface.Radius, 0, [0]),
            "forbes" => new ForbesQGeometry(surface.Radius, 0, 10, [0]),
            "grating" => new StandardGratingGeometry(surface.Radius, 0, 0, 1, 0),
            _ => new StandardGeometry(surface.Radius)
        };
        var parameters = surface.Geometry switch
        {
            EvenAsphereGeometry geometry => geometry.Base,
            OddAsphereGeometry geometry => geometry.Base,
            ForbesQGeometry geometry => geometry.Base,
            StandardGratingGeometry geometry => geometry.Base,
            _ => (StandardGeometry)surface.Geometry
        };
        var cache = new RayTraceCache(8, 10000);
        optic.ConfigureRayTraceCache(cache, 1);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(.3, .5, .5876, 32, "hexapolar");
        var request = TraceRequest.FinalOnly(normalizeOpticalPathDifference: false);
        using var first = optic.SequentialRayTracer.Trace(bundle, request);
        using var hit = optic.SequentialRayTracer.Trace(bundle, request);
        Assert.Equal(1, cache.Statistics.Hits);
        parameters.Radius *= 1.1;
        parameters.Conic = -.05;
        Assert.Equal(parameters.Radius, surface.Radius);
        Assert.Equal(parameters.Conic, surface.Conic);
        using var changed = optic.SequentialRayTracer.Trace(bundle, request);
        Assert.Equal(1, cache.Statistics.Hits);
        optic.InvalidateRayTraceCache();
        using var recomputed = optic.SequentialRayTracer.Trace(bundle, request);
        var last = optic.SurfaceGroup.Items.Count - 1;
        var compared = 0;
        for (var ray = 0; ray < bundle.Rays.Count; ray++)
            if (changed.TryGetSample(ray, last, out var actual) && recomputed.TryGetSample(ray, last, out var expected))
            {
                Assert.Equal(expected, actual);
                compared++;
            }
        Assert.True(compared > 0);
    }

    [Fact]
    public void ReplacingAnActiveBackendWithTheSameNameDetachesTheTraceCache()
    {
        var optic = Optic.CreateCookeTriplet();
        var cache = new RayTraceCache(8, 10000);
        optic.ConfigureRayTraceCache(cache, 1);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(0, 0, .5876, 16, "hexapolar");
        using var first = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly());
        using var hit = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly());
        Assert.Equal(1, cache.Statistics.Hits);
        var replacement = new ManagedCpuBackend();
        optic.Backend.Register(replacement);
        Assert.Same(replacement, optic.Backend.Current);
        using var changed = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly());
        Assert.Equal(1, cache.Statistics.Hits);
    }

    [Theory]
    [InlineData("quick")]
    [InlineData("radius")]
    [InlineData("variables")]
    public async Task OptimizationPreparationDoesNotBlockReadsAndRejectsConcurrentEdits(string method)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var workspace = new WorkspaceCoordinator(new OpticContext(Optic.CreateCookeTriplet()), _ =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        });
        workspace.Runtime.Surfaces[1].RadiusVariable = true;
        var optimization = new OptimizationService(workspace);
        Task pending = method switch
        {
            "radius" => optimization.OptimizeSurfaceRadiusAsync(1, "Damped Least Squares", 1),
            "variables" => optimization.OptimizeVariablesAsync("Damped Least Squares", 1),
            _ => optimization.QuickFocusAsync()
        };
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            // Measure document-lock availability, independently of a saturated test thread pool.
            await Task.Factory.StartNew(workspace.GetDocumentSnapshot, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TimeSpan.FromSeconds(2));
            workspace.MutateTransactional(WorkspaceChangeCategory.SystemSettings,
                () => workspace.Runtime.ReplaceMeritFunction([]), refreshAutomaticSemiDiameters: false);
            var expected = SerializeDocument(workspace.Runtime.CaptureDocument());
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(expected, SerializeDocument(workspace.Runtime.CaptureDocument()));
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadingAndCancellationRemainAvailableDuringTheActualFocusComputation(bool replaceDocument)
    {
        using var workspace = new WorkspaceCoordinator(new OpticContext(Optic.CreateCookeTriplet()));
        using var backend = new BlockingBackend();
        workspace.Runtime.CurrentOptic.Backend.Register(backend);
        var documents = new OpticalDocumentService(workspace);
        var prescription = new PrescriptionService(workspace);
        var optimization = new OptimizationService(workspace);
        using var cancellation = new CancellationTokenSource();
        var pending = optimization.QuickFocusAsync(cancellation.Token);
        try
        {
            Assert.True(backend.Started.Wait(TimeSpan.FromSeconds(5)));
            var rows = await Task.Factory.StartNew(prescription.GetSurfaces, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(rows.Count > 2);
            if (replaceDocument) documents.NewBlank();
            else cancellation.Cancel();
            var expected = SerializeDocument(workspace.Runtime.CaptureDocument());
            backend.Release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(expected, SerializeDocument(workspace.Runtime.CaptureDocument()));
            Assert.False(documents.GetSnapshot().CanUndo);
        }
        finally { backend.Release.Set(); }
    }

    [Fact]
    public void DefaultCoreImageSimulationCompletesForASupportedSmallFieldSystem()
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var field in optic.Fields) { field.X *= .01; field.Y *= .01; }
        var result = ImageSimulationEngine.Simulate(optic, new RgbImage(new double[1, 16, 16]));
        Assert.Equal(3, result.Simulated.Channels);
        Assert.True(result.Simulated.Width > 0);
        Assert.All(result.Simulated.Values.Cast<double>(), value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void ConvolutionChecksAnAlreadyCancelledComputation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => ImageSimulationEngine.SpatiallyVariableConvolution(
            new double[16, 16], [], new double[0, 16, 16], new double[3, 3]));
    }

    [Fact]
    public async Task ConvolutionObservesCancellationWhileComputing()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Task.Factory.StartNew(() =>
        {
            using var scope = ComputationCancellation.Push(cancellation.Token);
            var source = new double[384, 384];
            var kernel = new double[32, 32];
            started.SetResult();
            return ImageSimulationEngine.SpatiallyVariableConvolution(source, [], new double[0, 384, 384], kernel);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        // Run cancellation on a dedicated worker. Under the full suite a delayed
        // test continuation can resume after convolution finishes, even though
        // its nominal delay was only 20 ms. That did not test cancellation.
        var cancelDuringWork = Task.Factory.StartNew(() =>
        {
            started.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Thread.Sleep(20);
            var wasComputing = !pending.IsCompleted;
            cancellation.Cancel();
            return wasComputing;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(await cancelDuringWork);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ConvolutionRetainsItsKernelCenterAndBoundaryValues(int kernelSize)
    {
        var source = new double[3, 4];
        for (var row = 0; row < 3; row++) for (var column = 0; column < 4; column++) source[row, column] = row * 4 + column + 1;
        var kernel = new double[kernelSize, kernelSize];
        kernel[0, 0] = 1; kernel[kernelSize - 1, kernelSize - 1] = .5;
        var actual = ImageSimulationEngine.SpatiallyVariableConvolution(source, [], new double[0, 3, 4], kernel);
        var offset = (kernelSize - 1) / 2;
        double Value(int row, int column) => row >= 0 && row < 3 && column >= 0 && column < 4 ? source[row, column] : 0;
        for (var row = 0; row < 3; row++) for (var column = 0; column < 4; column++)
            Assert.Equal(Value(row + offset, column + offset)
                + .5 * Value(row + offset - kernelSize + 1, column + offset - kernelSize + 1), actual[row, column]);
    }

    [Fact]
    public void SimulationRejectsExpensiveConvolutionBeforeGeneratingAnyPsf()
    {
        var source = new RgbImage(new double[1, 512, 512]);
        var config = new ImageSimulationConfig
        {
            Padding = 0,
            PsfSize = 256,
            NumRays = 2,
            PsfGridRows = 1,
            PsfGridColumns = 1,
            Components = 1,
            WavelengthsMicrometers = [.55]
        };
        Assert.Contains("convolution", Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImageSimulationEngine.Simulate(Optic.CreateBlank(), source, config)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BudgetCountsAllWavelengthsAndEigenPsfConvolutions()
    {
        var source = new RgbImage(new double[1, 256, 256]);
        var config = new ImageSimulationConfig
        {
            Padding = 0,
            NumRays = 2,
            PsfSize = 32,
            PsfGridRows = 2,
            PsfGridColumns = 2,
            Components = 3,
            WavelengthsMicrometers = [.55]
        };
        AnalysisResourceLimits.ValidateImageSimulation(source, config);
        config = new ImageSimulationConfig
        {
            Padding = 0,
            NumRays = 2,
            PsfSize = 32,
            PsfGridRows = 2,
            PsfGridColumns = 2,
            Components = 3,
            WavelengthsMicrometers = [.55, .65]
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => AnalysisResourceLimits.ValidateImageSimulation(source, config));
    }

    [Fact]
    public void BudgetAlsoRejectsLargePsfDecompositionWithCheapRaySampling()
    {
        Assert.Contains("decomposition", Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnalysisResourceLimits.ValidatePsfConfiguration(new ImageSimulationConfig
            { PsfGridRows = 15, PsfGridColumns = 15, NumRays = 2, PsfSize = 16 })).Message);
    }

    [Fact]
    public void DecompositionBudgetCountsEveryWavelength()
    {
        var source = new RgbImage(new double[1, 16, 16]);
        AnalysisResourceLimits.ValidateImageSimulation(source, new ImageSimulationConfig
        { Padding = 0, PsfGridRows = 5, PsfGridColumns = 8, NumRays = 2, PsfSize = 16, WavelengthsMicrometers = [.55] });
        Assert.Contains("decomposition", Assert.Throws<ArgumentOutOfRangeException>(() =>
            AnalysisResourceLimits.ValidateImageSimulation(source, new ImageSimulationConfig
            { Padding = 0, PsfGridRows = 5, PsfGridColumns = 8, NumRays = 2, PsfSize = 16, WavelengthsMicrometers = [.55, .65] })).Message);
    }

    private static string SerializeDocument(LoadedOpticalDocument document) => JsonSerializer.Serialize(new
    {
        document.ActiveConfigurationIndex,
        Configurations = document.Configurations.Select(configuration => configuration.ToSnapshot()),
        document.BrokenLinks,
        document.NonSequentialDocument,
        document.OperandRows,
        document.OperandVariables,
        document.OperandPickups
    }, new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });

    private sealed class RecordingContext(Optic optic) : IOpticContext
    {
        private readonly OpticContext _inner = new(optic);
        public Thread? OpenThread;
        public ManualResetEventSlim CommitAttempt { get; } = new();
        public object RawGate => _inner.SyncRoot;
        public object SyncRoot { get { if (Thread.CurrentThread == OpenThread) CommitAttempt.Set(); return _inner.SyncRoot; } }
        public WorkbenchRuntime Runtime => _inner.Runtime;
        public CancellationTokenSource LinkDocumentToken(CancellationToken token) => _inner.LinkDocumentToken(token);
        public void CancelDocumentTasks() => _inner.CancelDocumentTasks();
        public void Dispose() { _inner.Dispose(); CommitAttempt.Dispose(); }
    }

    private sealed class BlockingBackend : INumericBackend, IDisposable
    {
        private readonly ManagedCpuBackend _inner = new();
        private int _blocked;
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public string Name => _inner.Name;
        public double Pi => _inner.Pi;
        public double Epsilon => _inner.Epsilon;
        public double Abs(double value) => _inner.Abs(value);
        public double Acos(double value) => _inner.Acos(value);
        public double Asin(double value) => _inner.Asin(value);
        public double Atan2(double y, double x) => _inner.Atan2(y, x);
        public double Cos(double value) => _inner.Cos(value);
        public double Exp(double value) => _inner.Exp(value);
        public double Log(double value) => _inner.Log(value);
        public double Pow(double value, double power) => _inner.Pow(value, power);
        public double Sin(double value) => _inner.Sin(value);
        public double Sqrt(double value) => _inner.Sqrt(value);
        public double Tan(double value) => _inner.Tan(value);
        public double Clamp(double value, double min, double max) => _inner.Clamp(value, min, max);
        public double Dot(Vector3D left, Vector3D right) => _inner.Dot(left, right);
        public Vector3D Cross(Vector3D left, Vector3D right) => _inner.Cross(left, right);
        public Vector3D Normalize(Vector3D vector)
        {
            if (Interlocked.Exchange(ref _blocked, 1) == 0)
            {
                Started.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            return _inner.Normalize(vector);
        }
        public void Dispose() { Started.Dispose(); Release.Dispose(); }
    }
}
