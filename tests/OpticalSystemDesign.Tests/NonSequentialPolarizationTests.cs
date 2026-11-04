using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Rays;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialPolarizationTests
{
    private static NonSequentialObjectDefinition Ray(string group, SystemPolarization polarization, double phase = 0, double x = 0) =>
        NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with
        {
            LocalCoordinateSystem = new(new(x, 0, 0)),
            Parameters = new SourceRayParameters { Physics = new(polarization, group, phase) }
        };
    private static NonSequentialObjectDefinition Detector(int pixels = 1, bool normalize = false) =>
        NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        { LocalCoordinateSystem = new(new(0, 0, 10)), Parameters = new DetectorRectangleParameters(4, 4, pixels, pixels, NormalizeCoherentPower: normalize) };
    private static NonSequentialDocument Doc(params NonSequentialObjectDefinition[] items) => new("Jones control",
        [new("d", 550, 1, true), new("red", 650, 1, false)], items, traceSettings: new(UsePolarization: true, CoherentDetection: true));
    private static NonSequentialDocumentTraceResult Trace(NonSequentialDocument doc) => new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials);

    [Theory]
    [InlineData(0, 2)] [InlineData(180, 0)] [InlineData(90, 1)]
    public void CoherentRaysRetainPhaseAndDeclaredPixelNormalization(double phase, double expected)
    {
        var doc = Doc(Ray("laser", new(false, 1, 0)), Ray("laser", new(false, 1, 0), phase), Detector());
        var result = Trace(doc); var frame = result.Detectors.Single(); var coherent = frame.CoherentByWavelength![1];
        Assert.Equal(2, frame.TotalPowerWatts, 12); Assert.Equal(2, result.EnergyBalance.SourcePowerWatts, 12);
        Assert.InRange(Math.Abs(expected - coherent.PowerWatts[0]), 0, 2e-12);
        Assert.InRange(Math.Abs(expected * 2 - coherent.RawPowerWatts[0]), 0, 4e-12);
        Assert.Equal(2, frame.StokesByWavelength![1].I[0], 12); Assert.Equal(2, frame.StokesByWavelength[1].Q[0], 12);
        Assert.All(result.Branches.SelectMany(b => b.Segments), s => Assert.NotNull(s.IncidentField));
    }

    [Fact]
    public void DifferentSourcesAndDifferentWavelengthsDoNotInterfereWithoutAnExplicitSharedGroup()
    {
        var first = Ray("a", new(false, 1, 0)); var second = Ray("b", new(false, 1, 0), 180);
        var frame = Trace(Doc(first, second, Detector())).Detectors.Single(); Assert.Equal(2, frame.CoherentByWavelength![1].PowerWatts[0], 12);
        var red = second with { Parameters = ((SourceRayParameters)second.Parameters) with { WavelengthNumber = 2, Physics = new(new(false, 1, 0), "a", 180) } };
        var both = Trace(Doc(first, red, Detector())).Detectors.Single();
        Assert.Equal(2, both.CoherentByWavelength!.Count); Assert.All(both.CoherentByWavelength.Values, v => Assert.Equal(1, v.PowerWatts[0], 12));
    }

    [Fact]
    public void StokesRotationCircularStateAndUnpolarizedModesArePhysical()
    {
        var frame = Trace(Doc(Ray("a", new(false, 1, 1, YPhaseDegrees: 90)), Detector())).Detectors.Single().StokesByWavelength![1];
        Assert.Equal(1, frame.I[0], 12); Assert.InRange(Math.Abs(frame.Q[0]), 0, 1e-14); Assert.Equal(-1, frame.V[0], 12);
        var unpolarized = Trace(Doc(Ray("a", new()), Detector())).Detectors.Single().StokesByWavelength![1];
        Assert.Equal(1, unpolarized.I[0], 12); Assert.InRange(Math.Abs(unpolarized.Q[0]) + Math.Abs(unpolarized.U[0]) + Math.Abs(unpolarized.V[0]), 0, 1e-13);
        var source = Ray("a", new(false, 1, 0)) with { LocalCoordinateSystem = new(Vector3D.Zero, RotationZDegrees: 45) };
        var rotated = Trace(Doc(source, Detector())).Detectors.Single().StokesByWavelength![1];
        Assert.InRange(Math.Abs(rotated.Q[0]), 0, 1e-13); Assert.Equal(1, rotated.U[0], 12);
    }

    [Fact]
    public void PixelCentrePhaseCancelsDifferentIntersectionsOnOnePlaneWave()
    {
        const double angle = .01; var k = new Vector3D(Math.Sin(angle), 0, Math.Cos(angle));
        var first = Ray("wave", new(false, 0, 1), x: -.1); var second = Ray("wave", new(false, 0, 1), x: .1);
        first = first with { Parameters = ((SourceRayParameters)first.Parameters) with { LocalDirection = k,
            Physics = new(new(false, 0, 1), "wave", -360 * .1 * Math.Sin(angle) / .00055) } };
        second = second with { Parameters = ((SourceRayParameters)second.Parameters) with { LocalDirection = k,
            Physics = new(new(false, 0, 1), "wave", 360 * .1 * Math.Sin(angle) / .00055) } };
        var frame = Trace(Doc(first, second, Detector())).Detectors.Single();
        Assert.Equal(2, frame.CoherentByWavelength![1].PowerWatts[0], 9);
    }

    [Fact]
    public void DatabaseV2ReconstructsJonesAndCoherentPixelsWhileLegacyV1RemainsReadable()
    {
        foreach (var enabled in new[] { false, true })
        {
            var doc = Doc(Ray("laser", new(false, 1, 1, YPhaseDegrees: 90)), Detector(4));
            doc.TraceSettings = doc.TraceSettings with { UsePolarization = enabled, CoherentDetection = enabled };
            using var stream = new MemoryStream();
            var header = NonSequentialRayDatabaseHeader.Create(doc); Assert.Equal(enabled ? 2 : 1, header.FormatVersion);
            NonSequentialDocumentTraceResult result;
            using (var writer = new NonSequentialRayDatabaseWriter(stream, header, true))
            { result = new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials, sink: writer); writer.Complete(); }
            stream.Position = 0; using var reader = new NonSequentialRayDatabaseReader(stream, true);
            var rebuilt = NonSequentialDetectorReconstruction.Reconstruct(doc, reader.ReadBranches()).Single();
            Assert.Equal(JsonSerializer.Serialize(result.Detectors.Single()), JsonSerializer.Serialize(rebuilt));
        }
    }

    [Fact]
    public void PhysicsBudgetAndUnsupportedRandomAmplitudePolicyFailExplicitly()
    {
        var doc = Doc(Ray("laser", new(false, 1, 0)), Detector());
        Assert.Throws<NotSupportedException>(() => new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials,
            new(SplittingMode: NonSequentialSplittingMode.SimpleStochastic)));
        Assert.Throws<InvalidOperationException>(() => Doc(Ray("laser", new(false, 1, 0)), Detector(1024)));
        var old = Doc(Ray("laser", new(false, 1, 0)), Detector()); old.TraceSettings = old.TraceSettings with { UsePolarization = false, CoherentDetection = false };
        Assert.Null(Trace(old).Detectors.Single().StokesByWavelength); Assert.Equal(1, NonSequentialRayDatabaseHeader.Create(old).FormatVersion);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void BareDielectricAtBrewsterAngleUsesPolarizedFluxAndConservesEnergy(bool pPolarized)
    {
        var materials = Optic.CreateBlank().Materials; const double n = 1.5; materials.Register(new OpticalSystemDesign.Core.Materials.ConstantIndexMaterial("ClearGlass", n));
        var theta = Math.Atan(n); var ray = Ray("beam", new(false, pPolarized ? 1 : 0, pPolarized ? 0 : 1));
        ray = ray with { LocalCoordinateSystem = new(new(0, 0, -10)), Parameters = ((SourceRayParameters)ray.Parameters) with
            { LocalDirection = new(Math.Sin(theta), 0, Math.Cos(theta)) } };
        var boundary = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
            { Parameters = new PlaneRectangleParameters(100, 100, NonSequentialSurfaceBehavior.Refractive, "Air", "ClearGlass") };
        var detector = Detector() with { Parameters = new DetectorRectangleParameters(100, 100, 1, 1) };
        var doc = Doc(ray, boundary, detector); var trace = new NonSequentialDocumentTracer().Trace(doc, materials);
        var rs = (1 - n * n) / (1 + n * n); var expected = pPolarized ? 1 : 1 - rs * rs;
        Assert.Equal(expected, trace.Detectors.Single().TotalPowerWatts, 10);
        Assert.Equal(1, trace.EnergyBalance.AccountedPowerWatts, 11);
        Assert.InRange(trace.EnergyBalance.AbsorbedPowerWatts, 0, 1e-12);
    }

    [Fact]
    public void TotalInternalReflectionRetainsDifferentialJonesPhaseAndUnitPower()
    {
        var materials = Optic.CreateBlank().Materials; const double n = 1.5; materials.Register(new OpticalSystemDesign.Core.Materials.ConstantIndexMaterial("ClearGlass", n));
        var theta = Math.PI / 3; var ray = Ray("beam", new(false, 1, 1)) with { LocalCoordinateSystem = new(new(0, 0, -1)) };
        ray = ray with { Parameters = ((SourceRayParameters)ray.Parameters) with { LocalDirection = new(Math.Sin(theta), 0, Math.Cos(theta)) } };
        var boundary = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
            { Parameters = new PlaneRectangleParameters(100, 100, NonSequentialSurfaceBehavior.Refractive, "ClearGlass", "Air") };
        var detector = Detector() with { LocalCoordinateSystem = new(new(0, 0, -10)), Parameters = new DetectorRectangleParameters(100, 100, 1, 1, FrontOnly: false) };
        var doc = new NonSequentialDocument("TIR", [new("d", 550, 1, true)], [ray, boundary, detector], "ClearGlass", new(UsePolarization: true, CoherentDetection: true));
        var result = new NonSequentialDocumentTracer().Trace(doc, materials); var frame = result.Detectors.Single();
        Assert.Equal(1, frame.TotalPowerWatts, 11); Assert.Equal(1, result.EnergyBalance.AccountedPowerWatts, 11);
        var c = Math.Cos(theta); var beta = Math.Sqrt(Math.Pow(Math.Sin(theta), 2) - 1 / (n * n));
        var rs = new System.Numerics.Complex(c, -beta) / new System.Numerics.Complex(c, beta);
        var rp = new System.Numerics.Complex(c / (n * n), -beta) / new System.Numerics.Complex(c / (n * n), beta);
        var expectedV = -c * (rp * System.Numerics.Complex.Conjugate(rs)).Imaginary;
        Assert.Equal(expectedV, frame.StokesByWavelength![1].V[0], 10);
        Assert.Equal(1, frame.StokesByWavelength[1].I[0] + frame.StokesByWavelength[1].LongitudinalPowerWatts[0], 11);
    }

    [Fact]
    public void BulkAbsorptionIsBeerLambertAndUnpolarizedTransverseBasisIsNonsingular()
    {
        var materials = Optic.CreateBlank().Materials;
        materials.Register(new OpticalSystemDesign.Core.Materials.ConstantIndexMaterial("WeakAbsorber", 1.5, 1e-8));
        var doc = new NonSequentialDocument("bulk", [new("d", 550, 1, true)], [Ray("a", new()), Detector()], "WeakAbsorber",
            new(UsePolarization: true, CoherentDetection: true));
        var result = new NonSequentialDocumentTracer().Trace(doc, materials); var expected = Math.Exp(-4 * Math.PI * 1e-8 * 10 / .00055);
        Assert.Equal(expected, result.Detectors.Single().TotalPowerWatts, 12);
        Assert.Equal(1 - expected, result.EnergyBalance.AbsorbedPowerWatts, 12); Assert.Equal(1, result.EnergyBalance.AccountedPowerWatts, 12);
        var sideways = Ray("u", new()) with { Parameters = new SourceRayParameters(LocalDirection: new(1, 0, 0)) };
        var sideDetector = Detector() with { LocalCoordinateSystem = new(new(10, 0, 0), RotationYDegrees: 90) };
        Assert.Equal(1, Trace(Doc(sideways, sideDetector)).Detectors.Single().TotalPowerWatts, 12);
    }

    [Fact]
    public void PolychromaticSourceSamplesDeclaredWeightsWithoutCrossWavelengthInterference()
    {
        var ray = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRectangle) with
        { Parameters = new SourceRectangleParameters(.1, .1, 0, WavelengthNumber: 0, AnalysisRayCount: 10000,
            AngularDistribution: NonSequentialSurfaceSourceAngularDistribution.VirtualPoint) { Physics = new(new(false, 1, 0), "laser") } };
        var doc = new NonSequentialDocument("spectrum", [new("a", 450, 1, true), new("zero", 550, 0, false), new("b", 650, 3, false)],
            [ray, Detector()], traceSettings: new(UsePolarization: true, CoherentDetection: true));
        var result = Trace(doc); var frame = result.Detectors.Single();
        Assert.Equal(2, frame.CoherentByWavelength!.Count); Assert.False(frame.PowerByWavelength.ContainsKey(2));
        Assert.InRange(frame.PowerByWavelength[1].Sum(), .23, .27); Assert.Equal(1, frame.TotalPowerWatts, 10);
        Assert.Equal(1, frame.CoherentByWavelength.Values.Sum(p => p.PowerWatts.Sum()), 9);
        Assert.Equal(NonSequentialDocumentTracer.SourceRayCount(doc), result.TotalBranchCount);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PolarizedMultilayerAssemblyPreservesReciprocalTransmissionWhenReversingTheFilm(bool unpolarized)
    {
        var optic = OpticalAssemblyTests.Prescription(glasses: 1);
        optic.SurfaceGroup.Items[1].CoatingModel = new OpticalSystemDesign.Core.Coatings.CoherentMultilayerCoating(
            [new(new OpticalSystemDesign.Core.Materials.ConstantIndexMaterial("L", 1.3), 70),
             new(new OpticalSystemDesign.Core.Materials.ConstantIndexMaterial("H", 2.2), 45)]);
        double Run(bool reverse)
        {
            var doc = OpticalAssemblyTests.Scene(optic); var detector = doc.Objects[^1];
            doc.Replace(detector.Id, detector with { LocalCoordinateSystem = new(new(0, 0, reverse ? -1 : .5)) });
            var source = Ray("film", new(unpolarized, 1, 1)) with { LocalCoordinateSystem = new(new(0, 0, reverse ? .25 : -2)) };
            source = source with { Parameters = ((SourceRayParameters)source.Parameters) with { LocalDirection = new(0, 0, reverse ? -1 : 1) } };
            doc.Insert(0, source); doc.TraceSettings = doc.TraceSettings with { UsePolarization = true, CoherentDetection = true };
            using var stream = new MemoryStream(); NonSequentialDocumentTraceResult result;
            using (var writer = new NonSequentialRayDatabaseWriter(stream, NonSequentialRayDatabaseHeader.Create(doc), true))
            { result = new NonSequentialDocumentTracer().Trace(doc, optic.Materials, new(SplittingMode: NonSequentialSplittingMode.None), writer); writer.Complete(); }
            Assert.Equal(1, result.EnergyBalance.AccountedPowerWatts, 11); Assert.InRange(result.Detectors.Single().TotalPowerWatts, .1, .999);
            stream.Position = 0; using var reader = new NonSequentialRayDatabaseReader(stream, true);
            Assert.Equal(JsonSerializer.Serialize(result.Detectors), JsonSerializer.Serialize(NonSequentialDetectorReconstruction.Reconstruct(doc, reader.ReadBranches())));
            return result.Detectors.Single().TotalPowerWatts;
        }
        Assert.Equal(Run(false), Run(true), 11);
    }
}
