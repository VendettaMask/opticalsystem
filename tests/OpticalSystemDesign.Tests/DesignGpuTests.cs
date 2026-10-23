using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class CudaFactAttribute : FactAttribute
{
    public CudaFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GPU_VALIDATION") != "1")
            Skip = "Requires explicit CUDA hardware validation.";
    }
}

public sealed class CudaTheoryAttribute : TheoryAttribute
{
    public CudaTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_GPU_VALIDATION") != "1")
            Skip = "Requires explicit CUDA hardware validation.";
    }
}

public sealed class DesignGpuTests
{
    [CudaFact]
    public void SharedRotationalIntersectionAndStopSolverExecuteOnCuda()
    {
        Assert.True(DesignGpu.Status.IsAvailable, DesignGpu.Status.UnavailableReason);
        var surfaces = new SequentialGpuSurface[]
        {
            new() { IndexAfter=1,Transmission=1,Reflection=1,ApertureBoundary=100 },
            new() { Equation=new() {Kind=2,Radius=100,Count=2,A1=1e-6},
                Origin=new(0,0,2),IndexAfter=1.5,Transmission=1,Reflection=1,ApertureBoundary=10 },
            new() { Origin=new(0,0,20),IndexAfter=1.5,Transmission=1,Reflection=1,ApertureBoundary=10,Backtrack=1 }
        };
        var rays = new SequentialGpuRay[] { new() {Origin=new(1,2,-1),Direction=new(0,0,1),Intensity=1,Valid=1,
            TargetX=1,TargetY=2,AimTolerance=2.5e-12} };
        var aimed=DesignGpu.Aim(surfaces,rays,1,default);
        Assert.Equal(1,aimed.Rays[0].Valid);
        var result=DesignGpu.Propagate(surfaces,aimed.Rays,false,2,default);
        Assert.Equal(1,result.Samples[2].Present);
        Assert.Equal(0,result.Samples[2].Blocked);
        Assert.InRange(Math.Abs(result.Samples[1].Point.X-1),0,1e-10);
        Assert.InRange(Math.Abs(result.Samples[1].Point.Y-2),0,1e-10);
        Assert.InRange(Math.Abs(result.Samples[2].Point.Z-20),0,1e-10);
    }

    [Fact]
    public void TapeRangeChecksCannotHideUnavailableTangentsAndScopesRestore()
    {
        Assert.Null(DifferentialTape.Current);
        using (var tape = new DifferentialTape(1))
        {
            var x = DifferentialScalar.Variable(-1, 3, 0, 1e308);
            Assert.Throws<GpuTapeRangeException>(() => DifferentialScalar.PositivePart(x * 1e308));
            Assert.Throws<GpuTapeRangeException>(() => x / 1e-200);
        }
        Assert.Null(DifferentialTape.Current);
        var cpu = DifferentialScalar.Variable(2, 1, 0);
        Assert.Equal(4, (cpu * cpu).Derivative(0));
    }

    [Fact]
    public void AutomaticModeReportsItsActualBackendAndCancellationIsImmediate()
    {
        var optic = OpticalDesignDerivativeTests.Lens(2, false, false);
        var parameters = OpticalDesignDerivativeTests.Parameters(false, false);
        var result = DifferentiableOpticalMetrics.Evaluate(optic, parameters, [0], [new(0, .5, 1)], computeMode: DesignComputeMode.Automatic);
        if (DesignGpu.Status.IsAvailable) Assert.True(result.Execution.GpuBatches > 0);
        else
        {
            Assert.Equal(DesignComputeExecution.Cpu.Backend, result.Execution.Backend);
            Assert.NotEmpty(result.Execution.FallbackReason!);
            Assert.Throws<NotSupportedException>(() => DifferentiableOpticalMetrics.Evaluate(optic, parameters, [0], [new(0, .5, 1)], computeMode: DesignComputeMode.Gpu));
        }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => DifferentiableOpticalMetrics.Evaluate(optic, parameters, [0], [new(0, .5, 1)], cancel.Token, DesignComputeMode.Gpu));
    }

    [CudaTheory]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    public void CudaOpticalGradientsMatchFormalCoreAndCpuTangents(int stop, bool asphere, bool relaxed)
    {
        var optic = OpticalDesignDerivativeTests.Lens(stop, asphere, relaxed);
        var parameters = OpticalDesignDerivativeTests.Parameters(asphere, relaxed);
        var pupils = ApertureSampler.GenerateGaussianQuadrature(2, 8).ToArray();
        var cpu = DifferentiableOpticalMetrics.Evaluate(optic, parameters, [0, .7, 1], pupils);
        var gpu = DifferentiableOpticalMetrics.Evaluate(optic, parameters, [0, .7, 1], pupils, computeMode: DesignComputeMode.Gpu);
        Assert.Equal("cuda-reverse-tape-v1", gpu.Execution.Backend);
        Assert.True(gpu.Execution.GpuBatches > 0);
        Assert.True(gpu.Execution.GpuRays > 0);
        Assert.Null(gpu.Execution.FallbackReason);
        Close(cpu.OpticalPowerPerMillimeter, gpu.OpticalPowerPerMillimeter);
        for (var index = 0; index < cpu.Fields.Count; index++)
        {
            var a = cpu.Fields[index]; var b = gpu.Fields[index];
            Close(a.CentroidX, b.CentroidX); Close(a.CentroidY, b.CentroidY);
            Close(a.RmsRadiusMillimeters, b.RmsRadiusMillimeters); Close(a.MaximumRadiusMillimeters, b.MaximumRadiusMillimeters);
            Close(a.ChiefRayDirectionZ, b.ChiefRayDirectionZ);
            for (var ray = 0; ray < a.Rays.Count; ray++)
            {
                Close(a.Rays[ray].X, b.Rays[ray].X); Close(a.Rays[ray].Y, b.Rays[ray].Y);
                Close(a.Rays[ray].DirectionZ, b.Rays[ray].DirectionZ);
                Close(a.Rays[ray].MinimumClearanceMillimeters, b.Rays[ray].MinimumClearanceMillimeters);
                Close(a.Rays[ray].MinimumBendCosine, b.Rays[ray].MinimumBendCosine);
            }
        }
        Assert.Equal(cpu.TracedRayCount, gpu.TracedRayCount);

        static void Close(DifferentialScalar expected, DifferentialScalar actual)
        {
            Assert.True(Math.Abs(expected.Value-actual.Value)<=2e-10*Math.Max(1,Math.Abs(expected.Value)),
                $"Nominal CPU {expected.Value:R}, CUDA {actual.Value:R}");
            Assert.Equal(expected.Dimension, actual.Dimension);
            for (var parameter = 0; parameter < expected.Dimension; parameter++)
                Assert.True(Math.Abs(expected.Derivative(parameter) - actual.Derivative(parameter)) <= 5e-8 * Math.Max(1, Math.Abs(expected.Derivative(parameter))),
                    $"Parameter {parameter}: CPU {expected.Derivative(parameter):R}, CUDA {actual.Derivative(parameter):R}");
        }
    }

    [CudaFact]
    public void HardwareCudaDifferentiatesRecordedCoreOperations()
    {
        Assert.True(DesignGpu.Status.IsAvailable, DesignGpu.Status.UnavailableReason);
        using var tape = new DifferentialTape(2);
        var x = DifferentialScalar.Variable(3, 4, 0);
        var y = DifferentialScalar.Variable(4, 4, 1);
        var z = DifferentialScalar.Sqrt(x * x + y * y);
        var result = DesignGpu.Differentiate([tape], [[z.TapeNode(tape), -1, -1, -1, -1]], 2, default);
        Assert.Equal(.6, result.Gradients[0], 12);
        Assert.Equal(.8, result.Gradients[1], 12);
        Assert.All(result.Gradients.Skip(2), value => Assert.Equal(0, value));
    }
}
