using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialExecutionTests
{
    [Fact]
    public async Task PausedTraceResumesWithExactlyTheSamePhysicalResult()
    {
        var document = Scene();
        var tracer = new NonSequentialDocumentTracer();
        var materials = new MaterialRegistry();
        var expected = tracer.Trace(document, materials);
        var control = new NonSequentialTraceControl();
        control.Pause();
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        NonSequentialTraceProgress? last = null;
        var progress = new Observer(value =>
        {
            last = value;
            if (value.IsPaused) paused.TrySetResult();
        });
        var task = Task.Run(() => tracer.Trace(document, materials,
            new() { Control = control, Progress = progress }));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(task.IsCompleted);
        control.Resume();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(expected.EnergyBalance, result.EnergyBalance);
        Assert.Equal(expected.Branches.Count, result.Branches.Count);
        Assert.Equal(expected.TotalSegmentCount, result.TotalSegmentCount);
        Assert.Equal(expected.Detectors[0].PowerByWavelength[1], result.Detectors[0].PowerByWavelength[1]);
        Assert.True(last!.IsCompleted);
        Assert.Equal(last.TotalSourceRays, last.LaunchedRays);
    }

    [Fact]
    public async Task CancellationInterruptsAPausedTrace()
    {
        var control = new NonSequentialTraceControl();
        control.Pause();
        using var cancellation = new CancellationTokenSource();
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Task.Run(() =>
        {
            using var scope = ComputationCancellation.Push(cancellation.Token);
            return new NonSequentialDocumentTracer().Trace(Scene(), new MaterialRegistry(),
                new() { Control = control, Progress = new Observer(_ => paused.TrySetResult()) });
        });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void BranchLookupHandlesNonMonotonicChunksAndIndexesOnlyTheMatchingDatabase()
    {
        var header = NonSequentialRayDatabaseHeader.Create(Scene());
        using var stream = new MemoryStream();
        Write(stream, header, Enumerable.Range(1, 1200).Reverse());
        using var reader = new NonSequentialRayDatabaseReader(stream, leaveOpen: true);
        var index = reader.BuildBranchIndex();
        Assert.Equal(777, reader.ReadBranchById(777, index)!.Id);
        Assert.Null(reader.ReadBranchById(1201, index));
        var children = reader.ReadChildBranches(1, index, offset: 5, count: 3);
        Assert.Equal(1199, children.Total);
        Assert.Equal(new long[] { 1195, 1194, 1193 }, children.Branches.Select(branch => branch.Id));

        using var other = new MemoryStream();
        Write(other, header, Enumerable.Range(1, 1200));
        using var second = new NonSequentialRayDatabaseReader(other, leaveOpen: true);
        Assert.False(second.IsBranchIndexCompatible(index));
        Assert.Throws<ArgumentException>(() => second.ReadBranchById(777, index));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => reader.BuildBranchIndex(cancellation.Token));
    }

    [Fact]
    public void ApplicationBranchNavigationReturnsParentChildrenAndRejectsAMissingId()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".starrdb");
        try
        {
            using (var stream = File.Create(path))
                Write(stream, NonSequentialRayDatabaseHeader.Create(Scene()), [1, 3, 2]);
            using var application = WorkbenchApplication.Create();
            var family = application.NonSequentialAnalysis.GetRayDatabaseBranch(path, 3);
            Assert.Equal(3, family.Branch.Id);
            Assert.Equal(1, family.Parent!.Id);
            Assert.Empty(family.Children);
            var root = application.NonSequentialAnalysis.GetRayDatabaseBranch(path, 1);
            Assert.Equal(new long[] { 3, 2 }, root.Children.Select(branch => branch.Id));
            Assert.Throws<KeyNotFoundException>(() => application.NonSequentialAnalysis.GetRayDatabaseBranch(path, 4));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ApplicationPauseResumeAndCancellationPreserveThePublishedDatabase()
    {
        using var app = WorkbenchApplication.Create();
        app.Modes.SwitchTo(Dto.OpticalWorkbenchMode.NonSequential);
        app.NonSequential.AddObject(Dto.NonSequentialObjectKind.SourcePoint);
        await app.NonSequentialAnalysis.TraceAsync(new(Dto.NonSequentialTraceOutputMode.RayDatabase, RayCountOverride: 1));
        var previous = app.NonSequentialAnalysis.GetCurrentSession()!;
        var bytes = File.ReadAllBytes(previous.RayDatabasePath!);
        using var cancellation = new CancellationTokenSource();
        var trace = app.NonSequentialAnalysis.TraceAsync(new(Dto.NonSequentialTraceOutputMode.RayDatabase,
            RayCountOverride: 1_000_000), cancellation.Token);
        await UntilAsync(() => app.NonSequentialAnalysis.GetTraceProgress() is not null);
        app.NonSequentialAnalysis.PauseTrace();
        Assert.True(app.NonSequentialAnalysis.GetTraceProgress()!.IsPaused);
        await Task.Delay(150);
        var paused = app.NonSequentialAnalysis.GetTraceProgress()!;
        await Task.Delay(150);
        Assert.Equal(paused.LaunchedRays, app.NonSequentialAnalysis.GetTraceProgress()!.LaunchedRays);
        app.NonSequentialAnalysis.ResumeTrace();
        await UntilAsync(() => app.NonSequentialAnalysis.GetTraceProgress() is { } value
            && !value.IsPaused && value.LaunchedRays > paused.LaunchedRays);
        app.NonSequentialAnalysis.PauseTrace();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await trace);
        Assert.Null(app.NonSequentialAnalysis.GetTraceProgress());
        Assert.Equal(previous.RayDatabasePath, app.NonSequentialAnalysis.GetCurrentSession()!.RayDatabasePath);
        Assert.Equal(bytes, File.ReadAllBytes(previous.RayDatabasePath!));
    }

    internal static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    internal static void Write(Stream stream, NonSequentialRayDatabaseHeader header, IEnumerable<int> ids)
    {
        using (var writer = new NonSequentialRayDatabaseWriter(stream, header, leaveOpen: true))
        {
            foreach (var id in ids)
                writer.OnBranch(new(id, id == 1 ? null : 1, id == 1 ? 0 : 1, null, [],
                    NonSequentialTerminationReason.Escaped, 1, 550));
            writer.Complete();
        }
        stream.Position = 0;
    }

    internal static NonSequentialDocument Scene() => new("Trace execution",
        [new("Primary", 550, 1, true)],
        [
            new(Guid.NewGuid(), "Source", NonSequentialObjectKind.SourceRay, true, true,
                new CoordinateSystem(Vector3D.Zero), null, null, new SourceRayParameters()),
            new(Guid.NewGuid(), "Detector", NonSequentialObjectKind.DetectorRectangle, true, true,
                new CoordinateSystem(new(0, 0, 10)), null, null, new DetectorRectangleParameters(10, 10, 8, 8))
        ]);

    private sealed class Observer(Action<NonSequentialTraceProgress> report) : IProgress<NonSequentialTraceProgress>
    {
        public void Report(NonSequentialTraceProgress value) => report(value);
    }
}
