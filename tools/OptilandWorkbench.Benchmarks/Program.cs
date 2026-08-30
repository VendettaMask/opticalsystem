using System.Diagnostics;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Tolerancing;

if (args.Length > 0 && args[0].Equals("--ci-smoke", StringComparison.OrdinalIgnoreCase))
{
    RunCiSmoke();
    return;
}

if (args.Length > 0 && args[0].Equals("--non-sequential", StringComparison.OrdinalIgnoreCase))
{
    var rayCount = args.Length > 1 ? int.Parse(args[1]) : 1_000_000;
    var databasePath = args.Length > 2
        ? Path.GetFullPath(args[2])
        : Path.Combine(Path.GetTempPath(), $"optiland-non-sequential-{rayCount}.starrdb");
    RunNonSequentialDatabase(rayCount, databasePath);
    return;
}

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

static void RunCiSmoke()
{
    const long maximumAllocatedBytes = 2L * 1024 * 1024 * 1024;
    const long maximumDatabaseBytes = 128L * 1024 * 1024;
    var maximumElapsed = TimeSpan.FromMinutes(2);
    var databasePath = Path.Combine(
        Path.GetTempPath(),
        $"optiland-ci-smoke-{Guid.NewGuid():N}.starrdb");

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    try
    {
        RunTrace(10_000, TraceRequest.FinalOnly(false), "CiFinalOnly");
        RunGeometricMtf(2_000);
        RunMonteCarlo(trials: 20, raysPerTrial: 64);
        RunNonSequentialDatabase(10_000, databasePath);
        var databaseBytes = new FileInfo(databasePath).Length;
        if (databaseBytes <= 0 || databaseBytes > maximumDatabaseBytes)
        {
            throw new InvalidOperationException(
                $"CI benchmark database size {databaseBytes:N0} is outside the expected range.");
        }
    }
    finally
    {
        stopwatch.Stop();
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    if (allocatedBytes > maximumAllocatedBytes)
    {
        throw new InvalidOperationException(
            $"CI benchmark allocated {allocatedBytes:N0} bytes, exceeding {maximumAllocatedBytes:N0}.");
    }
    if (stopwatch.Elapsed > maximumElapsed)
    {
        throw new InvalidOperationException(
            $"CI benchmark took {stopwatch.Elapsed}, exceeding {maximumElapsed}.");
    }

    Console.WriteLine(
        $"CI performance smoke passed in {stopwatch.Elapsed.TotalSeconds:F2}s with "
        + $"{allocatedBytes:N0} allocated bytes.");
}

static void RunNonSequentialDatabase(int rayCount, string databasePath)
{
    if (rayCount is <= 0 or > 1_000_000)
    {
        throw new ArgumentOutOfRangeException(nameof(rayCount), "Non-sequential benchmark rays must be between 1 and 1,000,000.");
    }

    var optic = Optic.CreateBlank();
    var document = NonSequentialDocument.CreateDefault(
        "Non-sequential database benchmark",
        new[] { new NonSequentialWavelength("d", 587.6, 1, true) });
    document.TraceSettings = document.TraceSettings with
    {
        AnalysisRaysPerSource = rayCount,
        MaximumTotalSourceRays = rayCount,
        SplitFresnelRays = false
    };
    document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourcePoint) with
    {
        Name = "Benchmark source",
        Parameters = new SourcePointParameters(
            ConeHalfAngleDegrees: 5,
            AnalysisRayCount: rayCount)
    });
    document.Insert(1, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
    {
        Name = "Benchmark detector",
        LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10)),
        Parameters = new DetectorRectangleParameters(
            WidthMillimeters: 100,
            HeightMillimeters: 100,
            PixelsX: 100,
            PixelsY: 100)
    });

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    if (File.Exists(databasePath)) File.Delete(databasePath);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    long branches;
    long segments;
    using (var stream = new FileStream(databasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    using (var writer = new NonSequentialRayDatabaseWriter(
        stream,
        NonSequentialRayDatabaseHeader.Create(document)))
    {
        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(OutputMode: NonSequentialTraceOutputMode.RayDatabase),
            writer);
        writer.Complete();
        branches = result.TotalBranchCount;
        segments = result.TotalSegmentCount;
    }
    stopwatch.Stop();

    var databaseBytes = new FileInfo(databasePath).Length;
    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var managedHeapBytes = GC.GetGCMemoryInfo().HeapSizeBytes;
    var peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64;
    var raysPerSecond = branches / stopwatch.Elapsed.TotalSeconds;
    Console.WriteLine("Mode,Rays,Branches,Segments,ElapsedMs,RaysPerSecond,AllocatedBytes,ManagedHeapBytes,ProcessPeakWorkingSetBytes,DatabaseBytes,DatabasePath");
    Console.WriteLine(
        $"NonSequentialRayDatabase,{rayCount},{branches},{segments},{stopwatch.Elapsed.TotalMilliseconds:F3},"
        + $"{raysPerSecond:F0},{allocatedBytes},{managedHeapBytes},{peakWorkingSetBytes},{databaseBytes},{databasePath}");
}

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
