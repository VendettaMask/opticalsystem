using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Tolerancing;

namespace OptilandWorkbench.Tests;

public sealed class HighPerformanceTracingTests
{
    [Fact]
    public void SelectedSurfaceTraceMatchesSerialAndParallelExecution()
    {
        var optic = Optic.CreateDemo();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(
            0.3,
            0.5,
            0.5876,
            128,
            "hexapolar");
        var finalSurface = optic.SurfaceGroup.Items.Count - 1;

        using var serial = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.Selected(new[] { finalSurface }, normalizeOpticalPathDifference: true) with
            {
                ParallelThreshold = int.MaxValue,
                MaxDegreeOfParallelism = 1
            });
        using var parallel = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.Selected(new[] { finalSurface }, normalizeOpticalPathDifference: true) with
            {
                ParallelThreshold = 1,
                MaxDegreeOfParallelism = 4
            });

        Assert.Equal(serial.RayCount, parallel.RayCount);
        for (var index = 0; index < serial.RayCount; index++)
        {
            var hasSerial = serial.TryGetSample(index, finalSurface, out var serialSample);
            var hasParallel = parallel.TryGetSample(index, finalSurface, out var parallelSample);
            Assert.Equal(hasSerial, hasParallel);
            if (!hasSerial)
            {
                continue;
            }

            Assert.Equal(serialSample.Position, parallelSample.Position);
            Assert.Equal(serialSample.Direction, parallelSample.Direction);
            Assert.Equal(serialSample.Intensity, parallelSample.Intensity);
            Assert.Equal(serialSample.CumulativeOpticalPathLength, parallelSample.CumulativeOpticalPathLength);
            Assert.Equal(serialSample.OpticalPathDifference, parallelSample.OpticalPathDifference);
            Assert.Equal(serialSample.Vignetted, parallelSample.Vignetted);
        }
    }

    [Fact]
    public void RequestedTraceDoesNotRecordSurfaceDataUnlessExplicitlyRequested()
    {
        var optic = Optic.CreateDemo();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(
            0,
            0,
            0.5876,
            16,
            "hexapolar");
        var finalSurface = optic.SurfaceGroup.Items.Count - 1;

        using (optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(new[] { finalSurface })))
        {
            Assert.Empty(optic.SurfaceGroup.RecordedTrace.Surfaces);
        }

        using (optic.SequentialRayTracer.Trace(
                   bundle,
                   TraceRequest.FullHistory(recordSurfaceData: true)))
        {
            Assert.Equal(optic.SurfaceGroup.Items.Count, optic.SurfaceGroup.RecordedTrace.SurfaceCount);
            Assert.Equal(bundle.Rays.Count, optic.SurfaceGroup.RecordedTrace.RayCount);
        }
    }

    [Fact]
    public void ManagedBatchBackendHandlesSimdTailAndTotalInternalReflection()
    {
        var backend = new ManagedCpuBackend();
        var batched = Assert.IsAssignableFrom<IBatchedNumericBackend>(backend);
        var x = new[] { 3.0, 0.0, 1.0, 0.0, 5.0 };
        var y = new[] { 4.0, 0.0, 0.0, 1.0, 0.0 };
        var z = new[] { 0.0, 0.0, 0.0, 0.0, 12.0 };
        var nx = new double[x.Length];
        var ny = new double[x.Length];
        var nz = new double[x.Length];

        batched.NormalizeDirections(x, y, z, nx, ny, nz);

        Assert.Equal(0.6, nx[0], 12);
        Assert.Equal(0.8, ny[0], 12);
        Assert.Equal(1.0, nz[1], 12);
        Assert.Equal(5.0 / 13.0, nx[^1], 12);
        Assert.Equal(12.0 / 13.0, nz[^1], 12);

        var sine = Math.Sqrt(0.75);
        var resultX = new double[1];
        var resultY = new double[1];
        var resultZ = new double[1];
        var kinds = new RayInteractionKind[1];
        batched.RefractOrReflect(
            new[] { sine },
            new[] { 0.0 },
            new[] { 0.5 },
            new[] { 0.0 },
            new[] { 0.0 },
            new[] { -1.0 },
            new[] { 1.5 },
            new[] { 1.0 },
            false,
            resultX,
            resultY,
            resultZ,
            kinds);

        Assert.Equal(RayInteractionKind.TotalInternalReflection, kinds[0]);
        Assert.True(resultZ[0] < 0);
        Assert.Equal(1.0, Math.Sqrt(
            (resultX[0] * resultX[0])
            + (resultY[0] * resultY[0])
            + (resultZ[0] * resultZ[0])), 12);
    }

    [Fact]
    public void ParallelMonteCarloIsDeterministicAcrossParallelismLevels()
    {
        var optic = Optic.CreateBlank();
        var monteCarlo = new MonteCarlo(optic, optic.CreateTolerancing());

        var serial = monteCarlo.RunDetailed(
            32,
            42,
            0,
            CancellationToken.None,
            CreateWorker,
            maxDegreeOfParallelism: 1);
        var parallel = monteCarlo.RunDetailed(
            32,
            42,
            0,
            CancellationToken.None,
            CreateWorker,
            maxDegreeOfParallelism: 4);
        var differentSeed = monteCarlo.RunDetailed(
            32,
            43,
            0,
            CancellationToken.None,
            CreateWorker,
            maxDegreeOfParallelism: 4);

        Assert.Equal(serial, parallel);
        Assert.NotEqual(
            serial.Select(result => result.CompensatedMerit),
            differentSeed.Select(result => result.CompensatedMerit));
    }

    private static Tolerancing CreateWorker(Optic worker)
    {
        var tolerancing = worker.CreateTolerancing();
        var variable = new DelegateVariable(
            "object spacing",
            () => worker.SurfaceGroup.Items[0].Thickness,
            value => worker.SurfaceGroup.Items[0].Thickness = value,
            0,
            1000);
        tolerancing.AddPerturbation(new VariablePerturbation(
            "uniform spacing",
            variable,
            new UniformSampler(-1, 1)));
        tolerancing.AddOperand(new Operand(
            "spacing target",
            100,
            1,
            () => worker.SurfaceGroup.Items[0].Thickness));
        tolerancing.SetCriterionEvaluator(() => worker.SurfaceGroup.Items[0].Thickness);
        return tolerancing;
    }
}
