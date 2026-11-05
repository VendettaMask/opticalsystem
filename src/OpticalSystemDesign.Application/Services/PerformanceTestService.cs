using System.Diagnostics;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Application.Services;

internal sealed class PerformanceTestService(WorkspaceCoordinator workspace, IWorkbenchModeService modes)
    : WorkbenchServiceBase(workspace), IPerformanceTestService
{
    private const int TrialCount = 3;
    private const int RaysPerTrial = 32_768;
    private const int MaximumBatchSamples = 131_072;
    private static readonly PupilSample[] ChiefPupil = [new(0, 0, 1)];

    public Task<PerformanceTestResult> RunAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Optic optic;
        int configuration;
        lock (Gate)
        {
            if (modes.CurrentMode != OpticalWorkbenchMode.Sequential)
                throw new NotSupportedException("Performance Test requires a sequential optical system.");
            optic = Runtime.CurrentOptic.Clone();
            configuration = Runtime.GetMultiConfigurationNavigation().ActiveIndex + 1;
        }

        return Measure(optic, configuration, cancellationToken);
    }, cancellationToken);

    private static PerformanceTestResult Measure(Optic optic, int configuration, CancellationToken token)
    {
        using var cancellation = ComputationCancellation.Push(token);
        using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
        optic.Backend.SetBackend("managed-cpu");
        optic.ConfigureRayTraceCache(null, 0);
        var surfaces = optic.SurfaceGroup.Items.Count;
        if (surfaces < 2) throw new InvalidOperationException("Performance Test requires an object and a final surface.");
        var wavelength = (optic.Wavelengths.FirstOrDefault(wave => wave.IsPrimary) ?? optic.Wavelengths.First()).Micrometers;
        var fieldIndex = Enumerable.Range(0, optic.Fields.Count)
            .MaxBy(index => Math.Pow(optic.Fields[index].X, 2) + Math.Pow(optic.Fields[index].Y, 2));
        var fields = optic.Fields.Select(field => FieldCoordinates.Normalize(optic.Fields, field.X, field.Y)).ToArray();
        var (hx, hy) = fields[fieldIndex];
        var batchSize = Math.Clamp(MaximumBatchSamples / surfaces, 1, 8192);
        var samples = ApertureSampler.GenerateSeededUniformDisk(RaysPerTrial, seed: 1234);
        var generator = optic.SequentialRayTracer.RayGenerator;
        var batches = samples.Chunk(batchSize)
            .Select(pupils => generator.GenerateNormalizedPupilSamples(hx, hy, wavelength, pupils))
            .ToArray();

        var visits = CountSurfaceVisits(optic, batches, surfaces, token);
        var request = TraceRequest.FinalOnly(normalizeOpticalPathDifference: false);
        using (optic.SequentialRayTracer.Trace(batches[0], request)) { }
        Prepare(optic.Clone(), fields, wavelength);

        var traces = new PerformanceTraceTrial[TrialCount];
        var updates = new PerformanceUpdateTrial[TrialCount];
        for (var trial = 0; trial < TrialCount; trial++)
            traces[trial] = MeasureTrace(optic, batches, request, surfaces - 1, visits, token);
        for (var trial = 0; trial < TrialCount; trial++)
        {
            var completed = 0;
            var elapsed = TimeSpan.Zero;
            do
            {
                token.ThrowIfCancellationRequested();
                var copy = optic.Clone();
                var start = Stopwatch.GetTimestamp();
                Prepare(copy, fields, wavelength);
                elapsed += Stopwatch.GetElapsedTime(start);
                completed++;
            }
            while (completed < 32 && elapsed < TimeSpan.FromMilliseconds(200));
            updates[trial] = new PerformanceUpdateTrial(completed, elapsed.TotalSeconds);
        }

        token.ThrowIfCancellationRequested();
        return new PerformanceTestResult(optic.Name, configuration, surfaces - 1, fieldIndex + 1, wavelength,
            Environment.ProcessorCount, traces, updates);
    }

    private static long CountSurfaceVisits(Optic optic, RealRayBundle[] batches, int surfaces, CancellationToken token)
    {
        var request = TraceRequest.FullHistory() with { NormalizeOpticalPathDifference = false };
        long visits = 0;
        foreach (var batch in batches)
        {
            token.ThrowIfCancellationRequested();
            using var result = optic.SequentialRayTracer.Trace(batch, request);
            // The launch/object slot is excluded. Terminal errors still consume one visited surface.
            for (var surface = 1; surface < surfaces; surface++)
                for (var ray = 0; ray < result.RayCount; ray++)
                    if (result.TryGetSample(ray, surface, out _)) visits++;
        }
        return visits;
    }

    private static PerformanceTraceTrial MeasureTrace(Optic optic, RealRayBundle[] batches,
        TraceRequest request, int finalSurface, long visits, CancellationToken token)
    {
        var elapsed = TimeSpan.Zero;
        var arrivals = 0;
        foreach (var batch in batches)
        {
            token.ThrowIfCancellationRequested();
            var start = Stopwatch.GetTimestamp();
            using var result = optic.SequentialRayTracer.Trace(batch, request);
            elapsed += Stopwatch.GetElapsedTime(start);
            for (var ray = 0; ray < result.RayCount; ray++)
                if (result.TryGetSample(ray, finalSurface, out var final) && !final.Vignetted && final.Intensity > 0)
                    arrivals++;
        }

        return new PerformanceTraceTrial(RaysPerTrial, arrivals, visits, elapsed.TotalSeconds);
    }

    private static void Prepare(Optic copy, (double X, double Y)[] fields, double primaryWavelength)
    {
        copy.Pickups.ApplyAll();
        copy.Solves.ApplyAll();
        copy.SurfaceGroup.Renumber();
        AutomaticSemiDiameterSolver.Update(copy);
        foreach (var wave in copy.Wavelengths)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            copy.Paraxial.EstimateEntrancePupilLocation(wave.Micrometers);
            copy.Paraxial.EstimateExitPupilLocation(wave.Micrometers);
        }
        foreach (var (hx, hy) in fields)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            copy.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                hx, hy, primaryWavelength, ChiefPupil, aimAtStop: copy.RayAimingEnabled);
        }
    }
}
