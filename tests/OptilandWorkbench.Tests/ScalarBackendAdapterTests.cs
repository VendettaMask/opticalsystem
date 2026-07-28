using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Tests;

public sealed class ScalarBackendAdapterTests
{
    [Fact]
    public void ScalarThirdPartyBackendAutomaticallyUsesBatchAdapter()
    {
        var optic = Optic.CreateDemo();
        optic.Backend.Register(new DelegatingScalarBackend());
        optic.Backend.SetBackend("test-scalar");
        Assert.False(optic.Backend.CurrentBatched.IsHardwareAccelerated);
        Assert.Equal(1, optic.Backend.CurrentBatched.PreferredBatchWidth);
        Assert.Same(optic.Backend.CurrentBatched, optic.Backend.CurrentBatched);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalized(
            0.2,
            0.4,
            0.5876,
            65,
            "hexapolar");
        var finalSurface = optic.SurfaceGroup.Items.Count - 1;

        using var adapted = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.FinalOnly(false) with { UseBatchedBackend = true });
        using var scalar = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.FinalOnly(false) with { UseBatchedBackend = false });

        for (var index = 0; index < adapted.RayCount; index++)
        {
            Assert.Equal(
                scalar.TryGetSample(index, finalSurface, out var expected),
                adapted.TryGetSample(index, finalSurface, out var actual));
            Assert.Equal(expected, actual);
        }
    }

    private sealed class DelegatingScalarBackend : INumericBackend
    {
        private readonly ManagedCpuBackend _inner = new();

        public string Name => "test-scalar";
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
        public double Clamp(double value, double min, double max) =>
            _inner.Clamp(value, min, max);
        public double Dot(Vector3D left, Vector3D right) => _inner.Dot(left, right);
        public Vector3D Cross(Vector3D left, Vector3D right) => _inner.Cross(left, right);
        public Vector3D Normalize(Vector3D vector) => _inner.Normalize(vector);
    }
}
