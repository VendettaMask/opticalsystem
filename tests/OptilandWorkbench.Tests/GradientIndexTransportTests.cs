using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Capabilities;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class GradientIndexTransportTests
{
    private static readonly Vector3D Forward = new(0, 0, 1);
    private static readonly RealRay Source = new(new(0, 0, -1), Forward, 550);

    [Fact]
    public void GaussianPupilIntegralPreservesFormalContinuousGrinPropagation()
    {
        var optic = Plate();
        var integral = optic.SequentialRayTracer.TraceGaussianPupil(new([Source])).Single();
        var physical = optic.SequentialRayTracer.Trace(new([Source])).RayHistories.Single();
        Assert.Equal(physical.Count, integral.Count);
        for (var i = 0; i < integral.Count; i++)
        {
            Near(physical[i].Position, integral[i].Position);
            Near(physical[i].Direction, integral[i].Direction);
            Near(physical[i].CumulativeOpticalPathLength, integral[i].CumulativeOpticalPathLength);
        }
        Assert.False(integral[^1].Vignetted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormalSequentialTraceUsesCurvedPathAndLocalExitIndex(bool batched)
    {
        var optic = Plate();
        using var trace = optic.SequentialRayTracer.Trace(new([Source]), Request(batched));
        var inside = Sample(trace, 2);
        var image = Sample(trace, 3);
        var exact = Catenary(1.5, 0.08, 10);
        var outgoingX = 1.5 * Math.Sinh(0.08 * 10 / 1.5);
        var outgoingZ = Math.Sqrt(1 - outgoingX * outgoingX);
        Assert.Equal(RayInteractionKind.Transmitted, inside.InteractionKind);
        Near(new(exact.X, 0, 10), inside.Position);
        Near(new(outgoingX, 0, outgoingZ), inside.Direction);
        Near(new(Math.Tanh(0.08 * 10 / 1.5), 0, 1 / Math.Cosh(0.08 * 10 / 1.5)), inside.IncidentDirection!.Value);
        Near(exact.Path, inside.SegmentLength);
        Near(exact.Opl, inside.SegmentOpticalPathLength);
        Near(exact.X + 5 * outgoingX / outgoingZ, image.Position.X);
        Near(1 + exact.Opl + 5 / outgoingZ, image.CumulativeOpticalPathLength);
        Near(image.CumulativeOpticalPathLength, image.PhaseInclusiveOpticalPathLength!.Value);
    }

    [Fact]
    public void EntranceInterfaceUsesIndexAtTheActualHitRatherThanTheBaseIndex()
    {
        var material = Material(new Gradient4IndexProfile(1.5, x1: 0.1));
        var entrance = new OpticalSurface { MaterialAfter = material };
        var ray = new RealRay(new(0, 0, -1), new Vector3D(0.6, 0, 0.8), 550);
        var result = entrance.TraceRay(ray, new AirMaterial(), material, 0, 0);
        var index = 1.5 + 0.1 * 0.75;
        Near(index, result.OutgoingRefractiveIndex);
        Near(0.6 / index, result.Ray.Direction.X);
        Assert.IsType<LocatedGradientIndexMaterial>(result.OutgoingMaterial);
    }

    [Fact]
    public void CoatingUsesTheLocalIndexAndCurvedIncidentDirection()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([]);
        optic.SurfaceGroup.Items[2].CoatingModel = new CoherentMultilayerCoating([]);
        using var trace = optic.SequentialRayTracer.Trace(new([Source]), Request());
        var a = 0.08 * 10 / 1.5;
        var n = 1.5 * Math.Cosh(a);
        var cosI = 1 / Math.Cosh(a);
        var sinT = 1.5 * Math.Sinh(a);
        var cosT = Math.Sqrt(1 - sinT * sinT);
        var rs = (n * cosI - cosT) / (n * cosI + cosT);
        var rp = (cosI - n * cosT) / (cosI + n * cosT);
        Near(0.96 * (1 - (rs * rs + rp * rp) / 2), Sample(trace, 2).Intensity);
    }

    [Fact]
    public void CurvedExitUsesItsTrueNormalAndLocalRefractiveIndex()
    {
        var optic = Plate(Material(new Gradient4IndexProfile(1.5, x1: 0.04)));
        optic.SurfaceGroup.Items[2].Geometry = new StandardGeometry(20);
        var lower = 10.0;
        var upper = 12.0;
        for (var iteration = 0; iteration < 60; iteration++)
        {
            var z = (lower + upper) / 2;
            var x = Catenary(1.5, 0.04, z).X;
            if (z > 30 - Math.Sqrt(400 - x * x)) upper = z;
            else lower = z;
        }
        var depth = (lower + upper) / 2;
        var exact = Catenary(1.5, 0.04, depth);
        var a = 0.04 * depth / 1.5;
        var incoming = new Vector3D(Math.Tanh(a), 0, 1 / Math.Cosh(a));
        var normal = new Vector3D(-exact.X / 20, 0, Math.Sqrt(400 - exact.X * exact.X) / 20);
        var tangent = incoming - normal * (incoming.X * normal.X + incoming.Z * normal.Z);
        tangent *= 1.5 * Math.Cosh(a);
        var expectedDirection = tangent + normal * Math.Sqrt(1 - tangent.Length * tangent.Length);
        using var trace = optic.SequentialRayTracer.Trace(new([Source]), Request());
        var exit = Sample(trace, 2);
        Near(new(exact.X, 0, depth), exit.Position);
        Near(exact.Opl, exit.SegmentOpticalPathLength);
        Near(expectedDirection, exit.Direction);
    }

    [Fact]
    public void ReflectionRetainsOriginalVolumeCoordinatesAndIntegratesReturnPath()
    {
        var optic = Plate(Material(new Gradient3IndexProfile(1.5, axial1: 0.01)));
        optic.SurfaceGroup.Items[2].IsReflective = true;
        optic.SurfaceGroup.Items[2].Thickness = -10;
        optic.SurfaceGroup.Items[3].Thickness = -3;
        optic.SurfaceGroup.Items.Add(new OpticalSurface());
        optic.SurfaceGroup.Renumber();
        using var trace = optic.SequentialRayTracer.Trace(new([Source]), Request());
        var mirror = Sample(trace, 2);
        var returned = Sample(trace, 3);
        Assert.Equal(RayInteractionKind.Reflected, mirror.InteractionKind);
        Assert.Equal(RayInteractionKind.Transmitted, returned.InteractionKind);
        Near(15.5, mirror.SegmentOpticalPathLength);
        Near(15.5, returned.SegmentOpticalPathLength);
        Near(-Forward, returned.Direction);
        Near(35, Sample(trace, 4).CumulativeOpticalPathLength);
    }

    [Fact]
    public void TotalInternalReflectionRetainsTheGradientFrame()
    {
        var material = Material(new Gradient4IndexProfile(1.5, x1: 0.15));
        var entry = new OpticalSurface().TraceRay(Source, new AirMaterial(), material, 0, 0);
        var back = new OpticalSurface { CoordinateSystem = new(new(0, 0, 10)) };
        var tir = back.TraceRay(entry.Ray, entry.OutgoingMaterial, new AirMaterial(), entry.CumulativePathLength, entry.CumulativeOpticalPathLength);
        Assert.Equal(RayInteractionKind.TotalInternalReflection, tir.InteractionKind);
        Assert.Same(entry.OutgoingMaterial, tir.OutgoingMaterial);
        var front = new OpticalSurface();
        var exited = front.TraceRay(tir.Ray, tir.OutgoingMaterial, new ConstantIndexMaterial("high n", 6),
            tir.CumulativePathLength, tir.CumulativeOpticalPathLength);
        var totalInside = Catenary(1.5, 0.15, 20);
        Near(new(totalInside.X, 0, 0), exited.Ray.Origin);
        Near(totalInside.Opl + 1, exited.CumulativeOpticalPathLength);
        Assert.Equal(RayInteractionKind.Transmitted, exited.InteractionKind);
    }

    [Fact]
    public void RotatedAndTranslatedVolumeHasItsOwnFrame()
    {
        var frame = new CoordinateSystem(new(4, 5, 6), 11, 23, -8);
        var material = Material(new Gradient4IndexProfile(1.5, x1: 0.08));
        var entrance = new OpticalSurface { CoordinateSystem = frame };
        var exit = new OpticalSurface { CoordinateSystem = frame with { Origin = frame.ToGlobalPoint(new(0, 0, 10)) } };
        var ray = Source with { Origin = frame.ToGlobalPoint(Source.Origin), Direction = frame.ToGlobalDirection(Forward) };
        var entry = entrance.TraceRay(ray, new AirMaterial(), material, 0, 0);
        var result = exit.TraceRay(entry.Ray, entry.OutgoingMaterial, new AirMaterial(), 1, 1);
        var exact = Catenary(1.5, 0.08, 10);
        var x = 1.5 * Math.Sinh(0.08 * 10 / 1.5);
        Near(frame.ToGlobalPoint(new(exact.X, 0, 10)), result.Ray.Origin);
        Near(frame.ToGlobalDirection(new(x, 0, Math.Sqrt(1 - x * x))), result.Ray.Direction);
        Near(exact.Opl, result.Sample.SegmentOpticalPathLength);
    }

    [Fact]
    public void AperturesAndDiagnosticsUseTheCurvedIntercept()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[2].PhysicalAperture = new CircularAperture(0.5);
        using var trace = optic.SequentialRayTracer.Trace(new([Source]), Request());
        var hit = Sample(trace, 2);
        Assert.True(hit.Vignetted);
        Assert.Equal(0, hit.Intensity);
        Assert.True(hit.Direction.X > 0.4);
        Assert.False(trace.TryGetSample(0, 3, out _));
        var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(Source);
        Assert.False(diagnostic.InsideAllApertures);
        Assert.Equal(2, diagnostic.FirstBlockedSurfaceIndex);
        Assert.NotNull(diagnostic.UnclippedImage);
        var circular = optic.SequentialRayTracer.DiagnoseCircularApertures(Source);
        Near(0.5 - Catenary(1.5, 0.08, 10).X, circular.MinimumClearanceMillimeters!.Value);
    }

    [Fact]
    public void IncidentDetectorStopsBeforeRefractionAndPolarizationIsExplicitlyRejected()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items.RemoveAt(3);
        var result = optic.SequentialRayTracer.DiagnoseApertures(Source, stopAtIncidentImage: true);
        Near(Math.Tanh(0.08 * 10 / 1.5), result.UnclippedImage!.Direction.X);
        Assert.Null(result.UnclippedImage.InteractionKind);
        Assert.Throws<NotSupportedException>(() => optic.SequentialRayTracer.DiagnoseApertures(Source, usePolarization: true));
        Assert.Throws<NotSupportedException>(() => optic.SequentialRayTracer.Trace(new RealRayBundle([
            Source with { PolarizationMatrix = Matrix3x3.Identity }]), Request()));
    }

    [Fact]
    public void SerialAndBatchedParallelRequestsAgreeForMultipleRays()
    {
        var optic = Plate();
        var bundle = new RealRayBundle(Enumerable.Range(0, 32).Select(i => Source with { Origin = new(-0.15 + i * 0.01, 0, -1) }));
        using var serial = optic.SequentialRayTracer.Trace(bundle, Request(false));
        using var parallel = optic.SequentialRayTracer.Trace(bundle, Request(true) with { MaxDegreeOfParallelism = 4, ParallelThreshold = 1 });
        for (var ray = 0; ray < bundle.Rays.Count; ray++)
        {
            Assert.True(serial.TryGetSample(ray, 3, out var a));
            Assert.True(parallel.TryGetSample(ray, 3, out var b));
            Near(a.Position, b.Position);
            Near(a.Direction, b.Direction);
            Near(a.CumulativeOpticalPathLength, b.CumulativeOpticalPathLength);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task EveryProfileAndNumericalSettingSurvivesCloneSnapshotAndStarOpt(int kind)
    {
        var material = new GradientIndexMaterial("saved GRIN", Profile(kind), 321,
            new(MaximumStep: 0.23, MinimumStep: 1e-11, PositionTolerance: 2e-10,
                DirectionTolerance: 3e-11, OpticalPathTolerance: 4e-10, RelativeTolerance: 5e-11, SurfaceTolerance: 6e-10, MaximumAttempts: 123456));
        var optic = Plate(material);
        var snapshot = optic.ToSnapshot();
        OpticSnapshotValidator.Validate(snapshot);
        var restored = Optic.FromSnapshot(snapshot);
        Check(Assert.IsType<GradientIndexMaterial>(material.Clone()));
        Check(Assert.IsType<GradientIndexMaterial>(restored.SurfaceGroup.Items[1].MaterialAfter));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new([optic], 0), path);
            var loaded = await StarOptProjectStore.LoadAsync(path);
            Check(Assert.IsType<GradientIndexMaterial>(loaded.Configurations[0].SurfaceGroup.Items[1].MaterialAfter));
        }
        finally { File.Delete(path); }

        void Check(GradientIndexMaterial actual)
        {
            Assert.Equal(material.Name, actual.Name);
            Assert.Equal(material.MaximumPathLength, actual.MaximumPathLength);
            Assert.Equal(material.IntegrationOptions, actual.IntegrationOptions);
            var a = ComponentSnapshotFactory.FromMaterial(material);
            var b = ComponentSnapshotFactory.FromMaterial(actual);
            Assert.Equal(a.Numbers.OrderBy(pair => pair.Key), b.Numbers.OrderBy(pair => pair.Key));
            Assert.Equal(a.Text.OrderBy(pair => pair.Key), b.Text.OrderBy(pair => pair.Key));
            foreach (var point in new[] { new Vector3D(0.1, -0.2, 1), new(1, 0.5, 2), new(0, 0, 0) })
                Assert.Equal(material.RefractiveIndex(point, 550), actual.RefractiveIndex(point, 550));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("type")]
    [InlineData("negativeBase")]
    [InlineData("step")]
    [InlineData("attempts")]
    [InlineData("budget")]
    [InlineData("name")]
    [InlineData("extraText")]
    [InlineData("child")]
    public void InvalidSpatialMaterialPayloadsAreRejectedWithoutDefaults(string mutation)
    {
        var snapshot = Plate().ToSnapshot();
        var component = snapshot.Surfaces[1].Components!.MaterialAfterComponent!;
        switch (mutation)
        {
            case "missing": component.Numbers.Remove("nx1"); break;
            case "extra": component.Numbers["unknown"] = 0; break;
            case "type": component.Text["profile"] = "gradient99"; break;
            case "negativeBase": component.Numbers["n0"] = -1; break;
            case "step": component.Numbers["maximumStep"] = 0; break;
            case "attempts": component.Numbers["maximumAttempts"] = 2.5; break;
            case "budget": component.Numbers["maximumPathLength"] = -1; break;
            case "name": component.Text.Remove("name"); break;
            case "extraText": component.Text["unknown"] = "foo"; break;
            case "child":
                var surfaces = snapshot.Surfaces.ToList();
                surfaces[1] = surfaces[1] with
                {
                    Components = surfaces[1].Components! with
                    { MaterialAfterComponent = component with { Children = new() { ["unexpected"] = ComponentSnapshot.Empty("air") } } }
                };
                snapshot = snapshot with { Surfaces = surfaces };
                break;
        }
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(snapshot));
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
    }

    [Fact]
    public void CacheIsSharedForEquivalentSnapshotsAndDetachedOnGradientReplacement()
    {
        var snapshot = Plate().ToSnapshot();
        var first = Optic.FromSnapshot(snapshot);
        var second = Optic.FromSnapshot(snapshot);
        var cache = new RayTraceCache(maximumEntries: 8, maximumSamples: 1000);
        first.ConfigureRayTraceCache(cache, 12);
        second.ConfigureRayTraceCache(cache, 12);
        var bundle = new RealRayBundle([Source]);
        using var original = first.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly(false));
        using var cached = second.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([3]));
        Assert.Equal(1, cache.Statistics.Hits);
        Assert.Equal(Sample(original, 3), Sample(cached, 3));
        second.SurfaceGroup.Items[1].MaterialAfter = Material(new Gradient4IndexProfile(1.5, x1: 0.04));
        using var changed = second.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly(false));
        Assert.Equal(1, cache.Statistics.Hits);
        Assert.True(Math.Abs(Sample(changed, 3).Position.X - Sample(original, 3).Position.X) > 1);
    }

    [Fact]
    public void UnsupportedConsumersAndLossyExportsFailRatherThanAssumingHomogeneousGlass()
    {
        var optic = Plate();
        var material = Assert.IsType<GradientIndexMaterial>(optic.SurfaceGroup.Items[1].MaterialAfter);
        Assert.Throws<NotSupportedException>(() => material.RefractiveIndex(550));
        Assert.Throws<NotSupportedException>(() => material.PropagationModel.Propagate(Source, 1));
        Assert.Throws<NotSupportedException>(() => optic.Paraxial.EstimateEffectiveFocalLength());
        Assert.Throws<OpticCapabilityException>(() => OpticCapabilityPreflight.EnsureSupported(optic, OpticCapabilityOperation.Visualization));
        Assert.Throws<NotSupportedException>(() => new NonSequentialRayTracer(optic).Trace(Source));
        Assert.Throws<NotSupportedException>(() => SequentialLensDocument.FromOptic(optic));
        var exit = new OpticalSurface { CoordinateSystem = new(new(0, 0, 10)) };
        var ray = Source with { Origin = Vector3D.Zero };
        Assert.Throws<NotSupportedException>(() => exit.TraceRay(ray, material, new AirMaterial(), 0, 0));
        var explicitFrame = exit.TraceRay(ray, material, new AirMaterial(), 0, 0, CoordinateSystem.Global);
        Near(Catenary(1.5, 0.08, 10).Opl, explicitFrame.Sample.SegmentOpticalPathLength);
        Assert.Throws<NotSupportedException>(() => ComponentSnapshotFactory.FromMaterial(material.At(CoordinateSystem.Global)));
        optic.SurfaceGroup.Items[0].MaterialAfter = material;
        Assert.Throws<NotSupportedException>(() => optic.SequentialRayTracer.Trace(new RealRayBundle([Source]), Request()));
    }

    [Fact]
    public void PathBudgetMissAndCancellationCannotBecomeSuccessfulImageSamples()
    {
        var optic = Plate(new("short budget", new Gradient4IndexProfile(1.5, x1: 0.08), 1));
        using var trace = optic.SequentialRayTracer.Trace(new([Source]), Request());
        Assert.True(Sample(trace, 2).Vignetted);
        Assert.False(trace.TryGetSample(0, 3, out _));
        using var source = new CancellationTokenSource();
        source.Cancel();
        using var scope = ComputationCancellation.Push(source.Token);
        Assert.Throws<OperationCanceledException>(() => optic.SequentialRayTracer.Trace(new RealRayBundle([Source]), Request()));
    }

    [Fact]
    public void MaterialRejectsUnboundedSettingsAndUnpersistableProfiles()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradientIndexMaterial("GRIN", Profile(3), double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradientIndexMaterial("GRIN", Profile(3), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GradientIndexMaterial("GRIN", Profile(3), 10, new(MaximumAttempts: 0)));
        Assert.Throws<NotSupportedException>(() => new GradientIndexMaterial("GRIN", Profile(3), 10, new(RetainPath: true)));
        Assert.Throws<NotSupportedException>(() => new GradientIndexMaterial("GRIN", new UnregisteredProfile(), 10));
    }

    private static Optic Plate(GradientIndexMaterial? material = null)
    {
        var optic = new Optic("GRIN trace test");
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        material ??= Material(new Gradient4IndexProfile(1.5, x1: 0.08));
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 10, MaterialAfter = material, IsStop = true, SemiDiameter = 40 },
            new OpticalSurface { Thickness = 5, MaterialBefore = material, SemiDiameter = 40 },
            new OpticalSurface { SemiDiameter = 40 }
        ]);
        return optic;
    }

    private static GradientIndexMaterial Material(ISpatialRefractiveIndex profile) => new("GRIN test", profile, 100);
    private static ISpatialRefractiveIndex Profile(int kind) => kind switch
    {
        1 => new Gradient1IndexProfile(1.5, 0.01, 0.001),
        2 => new Gradient2IndexProfile(2.25, 0.01, 0.0001, 0.00001, 0.000001, 0.0000001, 0.00000001),
        3 => new Gradient3IndexProfile(1.5, 0.01, 0.001, 0.0001, 0.02, 0.002, 0.0002),
        _ => new Gradient4IndexProfile(1.5, 0.02, 0.002, 0.03, 0.003, 0.04, 0.004)
    };
    private static TraceRequest Request(bool batched = false) => TraceRequest.FullHistory(maxDegreeOfParallelism: 1)
        with
    { NormalizeOpticalPathDifference = false, UseBatchedBackend = batched };
    private static RayTraceSampleValue Sample(RequestedTrace trace, int surface)
    {
        Assert.True(trace.TryGetSample(0, surface, out var sample));
        return sample;
    }
    private static (double X, double Path, double Opl) Catenary(double n, double g, double z)
    {
        var a = g * z / n;
        return (n / g * (Math.Cosh(a) - 1), n / g * Math.Sinh(a), n * n / (2 * g) * (a + Math.Sinh(a) * Math.Cosh(a)));
    }
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 3e-8);
    private static void Near(Vector3D expected, Vector3D actual) => Assert.InRange((expected - actual).Length, 0, 3e-8);
    private sealed class UnregisteredProfile : ISpatialRefractiveIndex
    {
        public double RefractiveIndex(Vector3D localPosition, double wavelengthNanometers) => 1.5;
        public SpatialIndexSample Evaluate(Vector3D localPosition, double wavelengthNanometers) => new(1.5, Vector3D.Zero);
    }
}
