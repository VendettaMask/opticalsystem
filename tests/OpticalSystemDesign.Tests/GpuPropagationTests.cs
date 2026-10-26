using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class GpuPropagationTests
{
    [CudaFact]
    public void DenseGridClippingBoundaryChecksAndDiagnosticMarginsMatchCpu()
    {
        var optic = OpticalDesignDerivativeTests.Lens(1, true, false);
        optic.SurfaceGroup.Items[2].PhysicalAperture = new CircularAperture(.8);
        var pupils = ApertureSampler.GenerateGaussianQuadrature(4, 16).ToArray();
        var checks = new[] { new PupilSample(0, 0, 1) }.Concat(ApertureSampler.Generate(24, PupilSampling.Ring)).ToArray();
        (double X, double Y)[] fields = [(0, 0), (0, .5), (0, 1)];
        SampledSpotAssessment[] cpu;
        SampledApertureDiagnosticResult cpuDiagnostic;
        using (var scope = new SequentialGpuSession(DesignComputeMode.Cpu))
        {
            cpu = fields.Select(f => SpotMetricEvaluator.EvaluatePupilSamplesWithChecks(optic, f.X, f.Y, pupils, checks, includeSurfaceTransmission: false)).ToArray();
            cpuDiagnostic = SampledApertureDiagnostics.Evaluate(optic, 0, 1, pupils);
        }
        using var gpuScope = new SequentialGpuSession(DesignComputeMode.Gpu);
        var gpu = Assert.IsAssignableFrom<IReadOnlyList<SampledSpotAssessment>>(SpotMetricEvaluator.EvaluateGpuPupilGridWithChecks(optic, fields, pupils, checks, includeSurfaceTransmission: false));
        for (var field = 0; field < fields.Length; field++)
        {
            Assert.Equal(cpu[field].Integration.RayCount, gpu[field].Integration.RayCount);
            Assert.Equal(cpu[field].Integration.VignettedRayCount, gpu[field].Integration.VignettedRayCount);
            Assert.True(gpu[field].Integration.VignettedRayCount > 0);
            Assert.Equal(cpu[field].Checks.VignettedRayCount, gpu[field].Checks.VignettedRayCount);
            Near(cpu[field].Integration.Metrics!.RmsSpotRadius, gpu[field].Integration.Metrics!.RmsSpotRadius, "grid RMS");
        }
        var gpuDiagnostic = SampledApertureDiagnostics.Evaluate(optic, 0, 1, pupils);
        Assert.Equal(cpuDiagnostic.IsComplete, gpuDiagnostic.IsComplete);
        for (var i = 0; i < cpuDiagnostic.Rays.Count; i++)
        {
            Assert.Equal(cpuDiagnostic.Rays[i].ReachedImage, gpuDiagnostic.Rays[i].ReachedImage);
            Near(cpuDiagnostic.Rays[i].MinimumClearanceMillimeters!.Value, gpuDiagnostic.Rays[i].MinimumClearanceMillimeters!.Value, "diagnostic clearance");
        }
        Assert.True(gpuScope.Execution.AimRays > 0); Assert.Equal(0, gpuScope.Execution.CpuRecoveryRays);
    }

    [CudaFact]
    public void TotalInternalReflectionRetainsIncidentMediumAndReturnOpticalPath()
    {
        var optic = new Optic("CUDA TIR return");
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() {Thickness=double.PositiveInfinity,Material="N-BK7",SemiDiameter=100},
            new() {Thickness=-2,Material="Air",SemiDiameter=100},new() {Material="Air",SemiDiameter=100}]);
        var bundle = new RealRayBundle([new(new(0, 0, -1), new(Math.Sin(Math.PI / 3), 0, Math.Cos(Math.PI / 3)), 587.6)]);
        RayTraceSampleValue[] cpu;
        using (var scope = new SequentialGpuSession(DesignComputeMode.Cpu)) cpu = Trace();
        using var gpuScope = new SequentialGpuSession(DesignComputeMode.Gpu);
        var gpu = Trace();
        Assert.Equal(OpticalSystemDesign.Core.Interactions.RayInteractionKind.TotalInternalReflection, gpu[0].InteractionKind);
        Assert.True(gpu[1].RefractiveIndexBefore > 1);
        Near(cpu[1].Position.X, gpu[1].Position.X, "TIR return X");
        Near(cpu[1].CumulativeOpticalPathLength, gpu[1].CumulativeOpticalPathLength, "TIR return OPL");
        Near(cpu[1].Intensity, gpu[1].Intensity, "TIR absorption");
        Assert.Equal(1, gpuScope.Execution.PropagatedRays);
        RayTraceSampleValue[] Trace()
        {
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([1, 2]) with { UseBatchedBackend = false });
            Assert.True(trace.TryGetSample(0, 1, out var a)); Assert.True(trace.TryGetSample(0, 2, out var b)); return [a, b];
        }
    }

    [CudaFact]
    public void UnsupportedGpuGeometryFallsBackOnlyInAutomaticAndScopesRestore()
    {
        Assert.Null(SequentialGpuSession.Current);
        var optic = OpticalDesignDerivativeTests.Lens(1, false, false);
        optic.SurfaceGroup.Items[1].CoordinateSystem = optic.SurfaceGroup.Items[1].CoordinateSystem with { RotationYDegrees = 2 };
        var bundle = new RealRayBundle([new(new(0, 0, -10), new(0, 0, 1), 587.6)]);
        using (var automatic = new SequentialGpuSession(DesignComputeMode.Automatic))
        {
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([3]));
            Assert.NotNull(automatic.Execution.FallbackReason); Assert.Equal(0, automatic.Execution.PropagatedRays);
        }
        using (var forced = new SequentialGpuSession(DesignComputeMode.Gpu))
            Assert.Throws<NotSupportedException>(() => optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([3])));
        Assert.Null(SequentialGpuSession.Current);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new SequentialGpuSession(DesignComputeMode.Gpu, cancelled.Token));
    }

    [CudaTheory]
    [InlineData(1, false, false, false)]
    [InlineData(2, false, false, false)]
    [InlineData(1, true, false, false)]
    [InlineData(2, true, true, false)]
    [InlineData(1, true, false, true)]
    [InlineData(2, true, true, true)]
    public void AimingAndEveryRetainedPhysicalHitMatchCpu(int stop, bool asphere, bool relaxed, bool nearFlat)
    {
        var optic = OpticalDesignDerivativeTests.Lens(stop, asphere, relaxed);
        if (nearFlat)
        { optic.SurfaceGroup.Items[1].Geometry = new OpticalSystemDesign.Core.Geometries.EvenAsphereGeometry(3000, 0, [0, 0]); }
        var pupils = ApertureSampler.GenerateGaussianQuadrature(3, 12).ToArray();
        var (cpuRays, cpu) = Trace(DesignComputeMode.Cpu);
        var (gpuRays, gpu) = Trace(DesignComputeMode.Gpu);
        Assert.Equal(cpu.Length, gpu.Length);
        for (var ray = 0; ray < cpuRays.Count; ray++)
        {
            Near(cpuRays[ray].Origin.X, gpuRays[ray].Origin.X, "launch X");
            Near(cpuRays[ray].Origin.Y, gpuRays[ray].Origin.Y, "launch Y");
            for (var i = 0; i < cpu.Length; i++)
            {
                Assert.Equal(cpu[i][ray]?.Vignetted, gpu[i][ray]?.Vignetted);
                if (cpu[i][ray] is not { } a || gpu[i][ray] is not { } b) continue;
                Near(a.Position.X, b.Position.X, $"surface {i} X"); Near(a.Position.Y, b.Position.Y, $"surface {i} Y"); Near(a.Position.Z, b.Position.Z, $"surface {i} Z");
                Near(a.Direction.X, b.Direction.X, $"surface {i} L"); Near(a.Direction.Y, b.Direction.Y, $"surface {i} M"); Near(a.Direction.Z, b.Direction.Z, $"surface {i} N");
                Near(a.Intensity, b.Intensity, $"surface {i} intensity"); Near(a.CumulativeOpticalPathLength, b.CumulativeOpticalPathLength, $"surface {i} OPL");
                Assert.Equal(a.InteractionKind, b.InteractionKind);
            }
        }

        (IReadOnlyList<RealRay>, RayTraceSampleValue?[][]) Trace(DesignComputeMode mode)
        {
            using var scope = new SequentialGpuSession(mode);
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 1, .5876, pupils, aimAtStop: true);
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(Enumerable.Range(0, optic.SurfaceGroup.Items.Count)));
            var data = Enumerable.Range(0, optic.SurfaceGroup.Items.Count).Select(i => trace.GetSurfaceSamples(i).ToArray()).ToArray();
            if (mode == DesignComputeMode.Gpu)
            { Assert.True(scope.Execution.AimRays > 0); Assert.True(scope.Execution.PropagatedRays > 0); Assert.Equal(0, scope.Execution.CpuRecoveryRays); }
            return (bundle.Rays, data);
        }
    }

    private static void Near(double a, double b, string quantity) => Assert.True(double.IsFinite(a) && double.IsFinite(b)
        && Math.Abs(a - b) <= 2e-9 * Math.Max(1, Math.Abs(a)), $"{quantity}: CPU {a:R}, GPU {b:R}");
}
