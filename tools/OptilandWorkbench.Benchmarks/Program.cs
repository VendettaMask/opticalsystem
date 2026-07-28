using System.Diagnostics;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Tolerancing;

var rayCounts = args.Length == 0
    ? new[] { 10_000, 100_000 }
    : args.Select(int.Parse).ToArray();

Console.WriteLine($"Runtime: {Environment.Version}");
Console.WriteLine($"CPU threads: {Environment.ProcessorCount}");
Console.WriteLine(
    "Mode,Rays,Surfaces,ElapsedMs,RaysPerSecond,AllocatedBytes,"
    + "ManagedHeapBytes,ProcessPeakWorkingSetBytes");

foreach (var rayCount in rayCounts)
{
    RunTrace(rayCount, TraceRequest.FinalOnly(false), "FinalOnly");
    RunTrace(rayCount, TraceRequest.Selected(new[] { 3 }, false), "SelectedSurface");
    RunTrace(rayCount, TraceRequest.FullHistory(), "FullHistory");
    RunGeometricMtf(rayCount);
}

RunMonteCarlo(trials: 100, raysPerTrial: 128);

static void RunTrace(int rayCount, TraceRequest request, string mode)
{
    var optic = CreateTwentySurfaceSystem();
    var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(
        0.5,
        0.5,
        0.5876,
        rayCount,
        "random");

    Measure(
        mode,
        bundle.Rays.Count,
        optic.SurfaceGroup.Items.Count,
        () =>
        {
            using var trace = optic.SequentialRayTracer.Trace(
                bundle,
                request with { ParallelThreshold = 1 });
            Consume(trace.RayCount);
        });
}

static void RunGeometricMtf(int rayCount)
{
    var optic = CreateTwentySurfaceSystem();
    var analysis = new GeometricMtfAnalysis(
        optic,
        numRays: rayCount,
        distribution: "random",
        numPoints: 128,
        wavelengthNumber: 2,
        fieldNumber: 1);
    Measure(
        "PsfMtfSampling",
        rayCount,
        optic.SurfaceGroup.Items.Count,
        () =>
        {
            var data = analysis.GenerateData();
            Consume(data.PlotSeries.Count);
        });
}

static void RunMonteCarlo(int trials, int raysPerTrial)
{
    var optic = CreateTwentySurfaceSystem();
    var tolerancing = CreateMonteCarloWorker(optic, raysPerTrial);
    var monteCarlo = new MonteCarlo(optic, tolerancing);
    Measure(
        $"MonteCarlo{trials}",
        trials * raysPerTrial,
        optic.SurfaceGroup.Items.Count,
        () =>
        {
            var results = monteCarlo.RunDetailed(
                trials,
                seed: 42,
                compensationIterations: 0,
                CancellationToken.None,
                worker => CreateMonteCarloWorker(worker, raysPerTrial),
                maxDegreeOfParallelism: Environment.ProcessorCount);
            Consume(results.Count);
        });
}

static Tolerancing CreateMonteCarloWorker(Optic optic, int raysPerTrial)
{
    var tolerancing = optic.CreateTolerancing();
    var surfaceNumber = optic.SurfaceGroup.Items[1].Number;
    var variable = new DelegateVariable(
        "stop spacing",
        () => FindSurface(optic, surfaceNumber).Thickness,
        value =>
        {
            FindSurface(optic, surfaceNumber).Thickness = value;
            SyncSurfacePositions(optic);
        },
        0,
        100,
        0.01);
    tolerancing.AddPerturbation(new VariablePerturbation(
        "stop spacing tolerance",
        variable,
        new NormalSampler(0, 0.01)));
    double Criterion()
    {
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(
            0.5,
            0.5,
            0.5876,
            raysPerTrial,
            "hexapolar");
        var samples = optic.SequentialRayTracer.TraceFinalSamples(bundle)
            .Where(sample => sample is { Vignetted: false, Intensity: > 0 })
            .Select(sample => sample!)
            .ToArray();
        if (samples.Length == 0)
        {
            return double.PositiveInfinity;
        }

        var centerX = samples.Average(sample => sample.Position.X);
        var centerY = samples.Average(sample => sample.Position.Y);
        return Math.Sqrt(samples.Average(sample =>
            Math.Pow(sample.Position.X - centerX, 2)
            + Math.Pow(sample.Position.Y - centerY, 2)));
    }

    tolerancing.AddOperand(new Operand("RMS spot", 0, 1, Criterion));
    tolerancing.SetCriterionEvaluator(Criterion);
    return tolerancing;
}

static void Measure(string mode, int rays, int surfaces, Action action)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    action();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    action();
    stopwatch.Stop();
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var raysPerSecond = stopwatch.Elapsed.TotalSeconds <= 0
        ? 0
        : rays / stopwatch.Elapsed.TotalSeconds;
    var managedHeap = GC.GetGCMemoryInfo().HeapSizeBytes;
    var peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64;
    Console.WriteLine(
        $"{mode},{rays},{surfaces},{stopwatch.Elapsed.TotalMilliseconds:F3},"
        + $"{raysPerSecond:F0},{allocated},{managedHeap},{peakWorkingSet}");
}

static Optic CreateTwentySurfaceSystem()
{
    var optic = Optic.CreateDemo();
    var source = optic.SurfaceGroup.Items
        .Where(surface => !surface.Label.Equals("Object", StringComparison.OrdinalIgnoreCase)
            && !surface.Label.Equals("Image", StringComparison.OrdinalIgnoreCase))
        .Select(surface => surface.Clone())
        .ToArray();
    var surfaces = new List<OpticalSurface>
    {
        optic.SurfaceGroup.Items[0].Clone()
    };
    while (surfaces.Count < 19)
    {
        surfaces.Add(source[(surfaces.Count - 1) % source.Length].Clone());
    }

    surfaces.Add(optic.SurfaceGroup.Items[^1].Clone());
    optic.SurfaceGroup.Replace(surfaces);
    return optic;
}

static OpticalSurface FindSurface(Optic optic, int surfaceNumber) =>
    optic.SurfaceGroup.Items.First(surface => surface.Number == surfaceNumber);

static void SyncSurfacePositions(Optic optic)
{
    var z = 0.0;
    foreach (var surface in optic.SurfaceGroup.Items)
    {
        surface.CoordinateSystem = surface.CoordinateSystem with
        {
            Origin = surface.CoordinateSystem.Origin with { Z = z }
        };
        z += surface.Thickness;
    }
}

static void Consume<T>(T value) => GC.KeepAlive(value);
