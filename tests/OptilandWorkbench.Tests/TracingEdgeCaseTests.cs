using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class TracingEdgeCaseTests
{
    [Fact]
    public void ReflectionKeepsAbsorbingIncidentMediumForReturnSegment()
    {
        const double extinction = 1e-7;
        const double wavelengthNanometers = 587.6;
        var optic = Optic.CreateBlank();
        var absorbing = new ConstantIndexMaterial(
            "absorbing glass",
            refractiveIndex: 1.5,
            extinctionCoefficient: extinction);
        optic.SurfaceGroup.Replace(
            new[]
            {
                Surface("Entrance", 0, absorbing),
                Surface("Mirror", 1, new AirMaterial(), reflective: true),
                Surface("Return", 0, new AirMaterial())
            });
        optic.SurfaceGroup.Items[0].CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 0));
        optic.SurfaceGroup.Items[1].CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 1));
        optic.SurfaceGroup.Items[2].CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 0));

        var bundle = Bundle(
            new Vector3D(0, 0, -1),
            new Vector3D(0, 0, 1),
            wavelengthNanometers);

        using var trace = optic.SequentialRayTracer.Trace(
            bundle,
            TraceRequest.FinalOnly(false));
        Assert.True(trace.TryGetSample(0, 2, out var sample));
        var wavelengthMicrometers = wavelengthNanometers / 1000.0;
        var expected = Math.Exp(
            (-4.0 * Math.PI * extinction * 2.0 * 1000.0)
            / wavelengthMicrometers);
        Assert.Equal(expected, sample.Intensity, precision: 12);
        Assert.Equal(4.0, sample.CumulativeOpticalPathLength, precision: 12);
    }

    [Fact]
    public void EarlyVignettingDoesNotInventSamplesOnLaterSurfaces()
    {
        var optic = Optic.CreateBlank();
        var clip = Surface("Clip", 0, new AirMaterial());
        clip.PhysicalAperture = new CircularAperture(0.1);
        optic.SurfaceGroup.Replace(
            new[]
            {
                clip,
                Surface("Image", 1, new AirMaterial())
            });

        using var trace = optic.SequentialRayTracer.Trace(
            Bundle(new Vector3D(1, 0, -1), new Vector3D(0, 0, 1)),
            TraceRequest.Selected(new[] { 0, 1 }));

        Assert.True(trace.TryGetSample(0, 0, out var clipped));
        Assert.True(clipped.Vignetted);
        Assert.False(trace.TryGetSample(0, 1, out _));
    }

    [Fact]
    public void InfiniteObjectDistanceUsesFiniteTraceCoordinates()
    {
        var optic = Optic.CreateBlank();
        var objectSurface = Surface("Object", double.PositiveInfinity, new AirMaterial());
        var image = Surface("Image", 10, new AirMaterial());
        optic.SurfaceGroup.Replace(
            new[] { objectSurface, image });
        optic.SurfaceGroup.Items[0].CoordinateSystem = new CoordinateSystem(Vector3D.Zero);
        optic.SurfaceGroup.Items[1].CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10));

        using var trace = optic.SequentialRayTracer.Trace(
            Bundle(new Vector3D(0, 0, 0), new Vector3D(0, 0, 1)),
            TraceRequest.Selected(new[] { 0, 1 }));

        Assert.True(trace.TryGetSample(0, 0, out var objectSample));
        Assert.Equal(0, objectSample.Position.Z, precision: 12);
        Assert.True(trace.TryGetSample(0, 1, out var sample));
        Assert.Equal(10, sample.Position.Z, precision: 12);
    }

    [Fact]
    public void RequestedTraceHonorsAmbientCancellation()
    {
        var optic = Optic.CreateDemo();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            optic.SequentialRayTracer.Trace(
                Bundle(new Vector3D(0, 0, -1), new Vector3D(0, 0, 1)),
                TraceRequest.FinalOnly(false)));
    }

    [Fact]
    public void ExceptionalSurfacePropagatesFailureAndReleasesTraceOwnership()
    {
        var optic = Optic.CreateBlank();
        var surface = Surface("Failure", 0, new AirMaterial());
        surface.Geometry = new ThrowingGeometry();
        optic.SurfaceGroup.Replace(new[] { surface });

        Assert.Throws<InvalidOperationException>(() =>
            optic.SequentialRayTracer.Trace(
                Bundle(new Vector3D(0, 0, -1), new Vector3D(0, 0, 1)),
                TraceRequest.FinalOnly(false)));

        surface.Geometry = new PlaneGeometry();
        using var recovered = optic.SequentialRayTracer.Trace(
            Bundle(new Vector3D(0, 0, -1), new Vector3D(0, 0, 1)),
            TraceRequest.FinalOnly(false));
        Assert.True(recovered.TryGetSample(0, 0, out _));
    }

    private static OpticalSurface Surface(
        string label,
        double z,
        IMaterial materialAfter,
        bool reflective = false) =>
        new()
        {
            Label = label,
            Geometry = new PlaneGeometry(),
            MaterialBefore = new AirMaterial(),
            MaterialAfter = materialAfter,
            InteractionModel = new RefractiveReflectiveInteractionModel(reflective),
            CoatingModel = new NoneCoatingModel(),
            CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, z))
        };

    private static RealRayBundle Bundle(
        Vector3D origin,
        Vector3D direction,
        double wavelengthNanometers = 587.6) =>
        new(new[] { new RealRay(origin, direction, wavelengthNanometers) });

    private sealed class ThrowingGeometry : IGeometry
    {
        public string Kind => "throwing-test";

        public double Sag(double x, double y) => 0;

        public double? DistanceToIntersection(Vector3D origin, Vector3D direction) =>
            throw new InvalidOperationException("Synthetic surface failure.");

        public Vector3D SurfaceNormal(Vector3D localPoint) => new(0, 0, 1);

        public IGeometry Clone() => new ThrowingGeometry();
    }
}
