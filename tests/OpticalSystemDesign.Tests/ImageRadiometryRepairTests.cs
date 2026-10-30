using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ImageRadiometryRepairTests
{
    [Theory]
    [InlineData(1, 1000)]
    [InlineData(3, 1000)]
    [InlineData(5, 10)]
    [InlineData(5, 100)]
    [InlineData(5, 1000)]
    [InlineData(9, 10)]
    [InlineData(9, 100)]
    [InlineData(9, -1000)]
    public void DistortionIsInvariantToJointTransverseTranslation(int degree, double offset)
    {
        var optic = Cone();
        var moved = Cone();
        var shift = new Vector3D(offset, -offset, 7);
        foreach (var surface in moved.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with { Origin = surface.CoordinateSystem.Origin + shift };
        var originalRay = optic.TraceGenericFinalSample(.2, .3, .25, .1, .55)!;
        var movedRay = moved.TraceGenericFinalSample(.2, .3, .25, .1, .55)!;
        Assert.InRange((movedRay.Position - originalRay.Position - shift).Length, 0, 1e-10);
        Assert.InRange((movedRay.Direction - originalRay.Direction).Length, 0, 1e-12);
        var expected = ImageSimulationEngine.Simulate(optic, Source(), Config(degree: degree));
        var actual = ImageSimulationEngine.Simulate(moved, Source(), Config(degree: degree));
        Assert.InRange(Difference(expected.Simulated.Values, actual.Simulated.Values), 0, 1e-8);
    }

    [Theory]
    [InlineData("Geometric", false, false)]
    [InlineData("Geometric", true, false)]
    [InlineData("Diffraction", false, false)]
    [InlineData("Diffraction", true, false)]
    [InlineData("Geometric", false, true)]
    [InlineData("Geometric", true, true)]
    [InlineData("Diffraction", false, true)]
    [InlineData("Diffraction", true, true)]
    public void EmptyGeometricPsfCannotBecomeAnIdealPoint(string mode, bool blocked, bool polarized)
    {
        var optic = Cone();
        if (blocked) optic.SurfaceGroup.Items[2].PhysicalAperture = new OffsetRadialAperture(.01, offsetX: 100);
        var exception = Assert.Throws<AnalysisDataUnavailableException>(() =>
            ImageSimulationEngine.GenerateBasis(optic, optic.Wavelengths[0], Config(mode, polarized: polarized)));
        Assert.Contains("PSF", exception.AnalysisName, StringComparison.Ordinal);
        Assert.Contains("wavelength", exception.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyPsfAnalysisReturnsUnavailableWithoutPublishingARaster()
    {
        var result = new ImageSimulationAnalysis(Cone(), new ImageSimulationConfig
        {
            SourceImage = Source(),
            PsfGridRows = 1,
            PsfGridColumns = 1,
            PsfSize = 8,
            NumRays = 8,
            Components = 1,
            Padding = 0,
            WavelengthsMicrometers = [.55],
            PixelSizeMillimeters = .01,
            FieldHeight = 1,
            AberrationMode = "Geometric",
            UseRelativeIllumination = false,
            DistortionGridSize = 3,
            DistortionPolynomialDegree = 1
        }).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome);
        Assert.Empty(result.PlotSeries);
        Assert.Null(result.PlotPanes);
    }

    [Fact]
    public void DeliberateNoAberrationModeStillUsesItsDeltaKernel()
    {
        var optic = Cone();
        var basis = ImageSimulationEngine.GenerateBasis(optic, optic.Wavelengths[0], Config());
        Assert.Equal(1, basis.MeanPsf.Cast<double>().Sum());
        Assert.Equal("None", Assert.Single(basis.EffectiveModes!));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, .25)]
    [InlineData(3, 0)]
    [InlineData(3, .25)]
    public void DetectorInteractionDoesNotAttenuateIncidentIrradiance(int detector, double transmission)
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[detector].PhysicalAperture = new CircularAperture(10);
        var expected = Irradiance(optic, detector);
        optic.SurfaceGroup.Items[detector].CoatingModel = new SimpleCoatingModel(transmission);
        var actual = Irradiance(optic, detector);
        Assert.Equal(expected.Values["PeakIrradiance"], actual.Values["PeakIrradiance"]);
        Assert.Equal(expected.Values["ValidRayCount"], actual.Values["ValidRayCount"]);
        Assert.Equal(expected.PlotSeries[0].Points, actual.PlotSeries[0].Points);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void IncidentIrradianceStillIncludesUpstreamLossAndTheDetectorMask(int detector)
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[detector].PhysicalAperture = new CircularAperture(10);
        var open = Irradiance(optic, detector);
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(.5);
        optic.SurfaceGroup.Items[detector].CoatingModel = new SimpleCoatingModel(0);
        var lossy = Irradiance(optic, detector);
        Assert.Equal(.5 * Convert.ToDouble(open.Values["PeakIrradiance"]),
            Convert.ToDouble(lossy.Values["PeakIrradiance"]), 12);
        optic.SurfaceGroup.Items[detector].PhysicalAperture = new OffsetRadialAperture(.01, offsetX: 100);
        var blocked = Irradiance(optic, detector);
        Assert.Equal(0, Convert.ToDouble(blocked.Values["PeakIrradiance"]));
        Assert.Equal(0, blocked.Values["ValidRayCount"]);
    }

    [Theory]
    [InlineData(15, 1)]
    [InlineData(45, 1)]
    [InlineData(80, 1)]
    [InlineData(15, 3)]
    [InlineData(45, 3)]
    [InlineData(80, 9)]
    public void AbsoluteAngularBinsReintegrateToTheirTracedPower(double halfAngle, int bins)
    {
        var result = new RadiantIntensityAnalysis(Cone(), bins, bins, -halfAngle, halfAngle,
            -halfAngle, halfAngle, numRays: 32, distribution: "sobol", normalize: false).GenerateData();
        var series = result.PlotPanes![0].Series[0];
        var step = 2 * halfAngle / bins;
        var integral = series.Points.Sum(point => point.Value!.Value * IndependentSolidAngle(
            point.X - step / 2, point.X + step / 2, point.Y - step / 2, point.Y + step / 2));
        Assert.Equal(Convert.ToDouble(result.Values["ValidRayCount"]), integral, 9);
        Assert.Equal(AnalysisAxisUnit.WattsPerSteradian, series.ValueUnit);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void IncidentTraceKeepsIncomingDirectionAndSeparatesCacheEntries(int detector, bool batched)
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[detector].PhysicalAperture = new CircularAperture(10);
        optic.SurfaceGroup.Items[detector].CoatingModel = new SimpleCoatingModel(.25);
        optic.SurfaceGroup.Items[detector].MaterialAfter = new ConstantIndexMaterial("detector medium", 2);
        var cache = new RayTraceCache();
        optic.ConfigureRayTraceCache(cache, 23);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(.2, .3, .55,
            ApertureSampler.Generate(128, PupilSampling.Sobol));
        var outgoingRequest = TraceRequest.Selected([detector]) with { UseBatchedBackend = batched, ParallelThreshold = 1 };
        var incidentRequest = TraceRequest.IncidentSurface(detector) with { UseBatchedBackend = batched, ParallelThreshold = 1 };
        using var outgoing = optic.SequentialRayTracer.Trace(bundle, outgoingRequest);
        using var incident = optic.SequentialRayTracer.Trace(bundle, incidentRequest);
        for (var ray = 0; ray < bundle.Rays.Count; ray++)
        {
            Assert.True(incident.TryGetSample(ray, detector, out var before));
            Assert.True(outgoing.TryGetSample(ray, detector, out var after));
            Assert.Equal(1, before.Intensity, 12);
            Assert.Equal(.25, after.Intensity, 12);
            Assert.Null(before.InteractionKind);
            Assert.Null(before.RefractiveIndexAfter);
            Assert.Equal(2, after.RefractiveIndexAfter);
            Assert.Equal(after.IncidentDirection, before.Direction);
            Assert.Equal(before.Position, after.Position);
        }
        Assert.Equal(2, cache.Statistics.EntryCount);
        var hits = cache.Statistics.Hits;
        using var cached = optic.SequentialRayTracer.Trace(bundle,
            incidentRequest with { StopAtIncidentSurfaceIndex = detector - optic.SurfaceGroup.Items.Count });
        Assert.Equal(hits + 1, cache.Statistics.Hits);
        Assert.Equal(incident.GetSurfaceSamples(detector), cached.GetSurfaceSamples(detector));
    }

    [Fact]
    public void IncidentTraceRejectsInconsistentRetentionAndNormalization()
    {
        var optic = Cone();
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, .55);
        Assert.Throws<ArgumentException>(() => optic.SequentialRayTracer.Trace(bundle,
            TraceRequest.Selected([3]) with { StopAtIncidentSurfaceIndex = 2 }));
        Assert.Throws<ArgumentException>(() => optic.SequentialRayTracer.Trace(bundle,
            TraceRequest.IncidentSurface(2) with { NormalizeOpticalPathDifference = true }));
        Assert.Throws<ArgumentException>(() => optic.SequentialRayTracer.Trace(bundle,
            TraceRequest.IncidentSurface(2) with { RecordSurfaceData = true }));
        Assert.Throws<ArgumentException>(() => optic.SequentialRayTracer.Trace(bundle,
            TraceRequest.Selected([2]) with { StopAtIncidentSurfaceIndex = 99 }));
    }

    [Theory]
    [InlineData(-45, 45, -45, 45)]
    [InlineData(25, 60, -40, 10)]
    [InlineData(-70, -20, 35, 80)]
    [InlineData(-.001, .001, -.002, .002)]
    public void ExactSolidAnglesMatchIndependentSphericalTriangles(double x0, double x1, double y0, double y1)
    {
        Assert.Equal(IndependentSolidAngle(x0, x1, y0, y1),
            ProjectedAngleSolidAngle.RectangleDegrees(x0, x1, y0, y1), 12);
    }

    [Fact]
    public void ProjectedAngularChartHandlesHemisphereBoundariesAndBackwardDirections()
    {
        Assert.Equal(4 * Math.PI, ProjectedAngleSolidAngle.RectangleDegrees(-180, 180, -180, 180), 12);
        Assert.Equal(2 * Math.PI, ProjectedAngleSolidAngle.RectangleDegrees(-90, 90, -90, 90), 12);
        Assert.Equal(Math.PI / 2, ProjectedAngleSolidAngle.RectangleDegrees(90, 180, 90, 180), 12);
        Assert.Equal(0, ProjectedAngleSolidAngle.RectangleDegrees(0, 45, 120, 150));
        Assert.Equal(0, ProjectedAngleSolidAngle.RectangleDegrees(181, 200, -45, 45));
        var sum = 0.0;
        for (var x = -180; x < 180; x += 20)
            for (var y = -180; y < 180; y += 20)
                sum += ProjectedAngleSolidAngle.RectangleDegrees(x, x + 20, y, y + 20);
        Assert.Equal(4 * Math.PI, sum, 11);
    }

    [Fact]
    public void RawAngularPowerIsNotMislabeledAsWattsPerSteradian()
    {
        var result = new RadiantIntensityAnalysis(Cone(), 3, 3, useAbsoluteUnits: false,
            numRays: 32, distribution: "sobol", normalize: false).GenerateData();
        Assert.Equal(AnalysisAxisUnit.Watt, result.PlotPanes![0].Series[0].ValueUnit);
        Assert.Equal(AnalysisAxisUnit.Watt, result.PlotPanes[1].Series[0].YUnit);
        Assert.Equal(Convert.ToDouble(result.Values["ValidRayCount"]),
            result.PlotPanes[0].Series[0].Points.Sum(point => point.Value!.Value), 12);
    }

    [Fact]
    public void BackwardTracedPowerIsIncludedInTheFullSphereBin()
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[1].IsReflective = true;
        optic.SurfaceGroup.Items[1].Thickness = -2;
        optic.SurfaceGroup.Items[2].Thickness = -8;
        var result = new RadiantIntensityAnalysis(optic, 1, 1, -180, 180, -180, 180,
            numRays: 32, distribution: "sobol", normalize: false).GenerateData();
        Assert.Equal(32, result.Values["ValidRayCount"]);
        Assert.Equal(32, result.PlotPanes![0].Series[0].Points.Single().Value!.Value * 4 * Math.PI, 10);
    }

    [Fact]
    public void SubthresholdSolidAngleStillReportsPhysicalDensity()
    {
        var optic = Cone();
        optic.SurfaceGroup.Items[0].Thickness = double.PositiveInfinity;
        optic.FieldDefinition = FieldDefinitionKind.Angle;
        optic.Fields[0].Y = 0;
        const double halfAngle = 1e-5;
        var result = new RadiantIntensityAnalysis(optic, 1, 1, -halfAngle, halfAngle, -halfAngle, halfAngle,
            numRays: 32, distribution: "sobol", normalize: false).GenerateData();
        var actual = result.PlotPanes![0].Series[0].Points.Single().Value!.Value;
        var expected = 32 / IndependentSolidAngle(-halfAngle, halfAngle, -halfAngle, halfAngle);
        Assert.InRange(Math.Abs(actual / expected - 1), 0, 1e-12);
    }

    private static AnalysisData Irradiance(Optic optic, int detector) => new IncoherentIrradianceAnalysis(
        optic, numRays: 32, resolutionX: 9, resolutionY: 9, detectorSurfaceIndex: detector,
        distribution: "sobol", normalize: false).GenerateData();

    [Fact]
    public void ExplicitSurfaceWindowBinsIncidentPowerWithoutAddingAPhysicalAperture()
    {
        var optic = Cone();
        var detector = optic.SurfaceGroup.Items[^1];
        detector.PhysicalAperture = null;
        detector.SemiDiameter = 10;
        Assert.Equal(AnalysisOutcome.Unavailable, new IncoherentIrradianceAnalysis(optic).GenerateData().Outcome);
        var result = new IncoherentIrradianceAnalysis(optic, 32, 9, 9, distribution: "sobol",
            normalize: false, useSurfaceSamplingWindow: true).GenerateData();
        Assert.NotEqual(AnalysisOutcome.Unavailable, result.Outcome);
        Assert.Contains("sampling window", result.Values["DetectorWindowSource"].ToString());
        var series = Assert.Single(result.PlotPanes!).Series[0];
        Assert.Equal(AnalysisAxisUnit.WattsPerSquareMillimeter, series.ValueUnit);
        // This unit-power, lossless cone sends every one of its 32 rays into
        // the 20-by-20-mm window. Integrating bins must recover that power.
        Assert.Equal(32, result.Values["ValidRayCount"]);
        Assert.Equal(32, series.Points.Sum(point => point.Value!.Value) * 400 / 81, 10);
        Assert.Null(detector.PhysicalAperture);
        Assert.Equal(10, detector.SemiDiameter);
        Assert.False(detector.SemiDiameterDefinesPhysicalAperture);
    }

    [Fact]
    public void SurfaceWindowOptionStillPrefersTheActualDetectorMask()
    {
        var optic = Cone();
        var detector = optic.SurfaceGroup.Items[^1];
        var aperture = new OffsetRadialAperture(.01, offsetX: 100);
        detector.PhysicalAperture = aperture;
        var result = new IncoherentIrradianceAnalysis(optic, 32, 9, 9, distribution: "sobol",
            normalize: false, useSurfaceSamplingWindow: true).GenerateData();
        Assert.Equal("Physical aperture", result.Values["DetectorWindowSource"]);
        Assert.Equal(0, result.Values["ValidRayCount"]);
        Assert.Equal(0, Convert.ToDouble(result.Values["PeakIrradiance"]));
        Assert.Same(aperture, detector.PhysicalAperture);
    }

    [Fact]
    public void SurfaceWindowOptionDoesNotBypassUnsupportedPhysicalApertures()
    {
        var optic = Cone();
        var aperture = new UnsupportedDetectorAperture();
        optic.SurfaceGroup.Items[^1].PhysicalAperture = aperture;
        var result = new IncoherentIrradianceAnalysis(optic, useSurfaceSamplingWindow: true).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome);
        Assert.Empty(result.PlotSeries);
        Assert.Same(aperture, optic.SurfaceGroup.Items[^1].PhysicalAperture);
    }

    private sealed class UnsupportedDetectorAperture : IPhysicalAperture
    {
        public string Kind => "unsupported-test-aperture";
        public bool Contains(Vector3D point) => true;
        public IPhysicalAperture Clone() => new UnsupportedDetectorAperture();
    }

    internal static double IndependentSolidAngle(double x0, double x1, double y0, double y1)
    {
        static Vector3D Corner(double x, double y)
        {
            var vector = new Vector3D(Math.Tan(x * Math.PI / 180), Math.Tan(y * Math.PI / 180), 1);
            return vector / vector.Length;
        }
        static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static double Triangle(Vector3D a, Vector3D b, Vector3D c)
        {
            var cross = new Vector3D(b.Y * c.Z - b.Z * c.Y, b.Z * c.X - b.X * c.Z, b.X * c.Y - b.Y * c.X);
            return 2 * Math.Atan2(Math.Abs(Dot(a, cross)), 1 + Dot(a, b) + Dot(b, c) + Dot(c, a));
        }
        var a = Corner(x0, y0);
        var b = Corner(x1, y0);
        var c = Corner(x1, y1);
        var d = Corner(x0, y1);
        return Triangle(a, b, c) + Triangle(a, c, d);
    }

    internal static Optic Cone()
    {
        var optic = new Optic { FieldDefinition = FieldDefinitionKind.ObjectHeight };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint { Y = 2 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 10 },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 2 },
            new OpticalSurface { Thickness = 8 },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static RgbImage Source()
    {
        var values = new double[1, 16, 16];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++) values[0, y, x] = .1 + .5 * x / 15 + .3 * y / 15;
        return new(values);
    }

    private static ImageSimulationConfig Config(string mode = "None", int degree = 1, bool polarized = false) => new()
    {
        ImageWidth = 16,
        ImageHeight = 16,
        Padding = 0,
        WavelengthsMicrometers = [.55],
        PsfGridRows = 1,
        PsfGridColumns = 1,
        PsfSize = 8,
        NumRays = 8,
        Components = 1,
        PixelSizeMillimeters = .01,
        FieldHeight = 1,
        AberrationMode = mode,
        UsePolarization = polarized,
        UseRelativeIllumination = false,
        DistortionGridSize = 9,
        DistortionPolynomialDegree = degree
    };

    private static double Difference(Array a, Array b) => a.Cast<double>().Zip(b.Cast<double>(), (x, y) => Math.Abs(x - y)).Max();
}
