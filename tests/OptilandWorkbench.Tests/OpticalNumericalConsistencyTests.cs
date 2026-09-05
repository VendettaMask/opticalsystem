using System.Numerics;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

public sealed class OpticalNumericalConsistencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RayFanUsesTheSystemRayAimingSetting(bool aiming)
    {
        var optic = Baseline();
        optic.RayAimingEnabled = aiming;
        var data = new RayFanAnalysis(optic, numPoints: 5, wavelengthNumber: 2,
            fieldNumber: 2, vignettedPupil: true, zemaxCompatible: true).GenerateData();
        double ImageY(double pupilY)
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, 0, pupilY,
                optic.Wavelengths[1].Micrometers, aimAtStop: aiming);
            using var trace = optic.SequentialRayTracer.Trace(bundle,
                OptilandWorkbench.Core.Raytrace.TraceRequest.Selected(new[] { optic.SurfaceGroup.Items.Count - 1 }));
            return trace.GetSurfaceSamples(optic.SurfaceGroup.Items.Count - 1)[0]!.Value.Position.Y;
        }
        Assert.Equal(ImageY(.5) - ImageY(0), data.PlotPanes![0].Series[0].Points[3].Y, 11);
    }

    [Fact]
    public void FrequencyLimitInterpolatesComplexOtfAndPreservesDirectionalAxes()
    {
        var otf = new[] { Complex.One, -Complex.One };
        var result = DiffractionEngine.LimitFrequency(new MtfResult(new[] { 0.0, 1.0 },
            new[] { 1.0, 1.0 }, new[] { 1.0, 1.0 }, 2, otf, otf,
            new[] { 0.0, 1.0 }, new[] { 0.0, 2.0 }), .5);
        Assert.Equal(0, result.Tangential[^1]);
        Assert.Equal(0, result.Sagittal[^1]);
        Assert.Equal(.5, result.TangentialFrequency![^1]);
        Assert.Equal(1, result.SagittalFrequency![^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FftOtfPhaseUsesPhysicalPsfOrigin(int displacement)
    {
        var optic = Optic.CreateCookeTriplet();
        var values = new double[16, 16];
        values[8 + displacement, 8] = 1;
        var mtf = DiffractionEngine.ComputeFftMtf(new PsfResult(values, 8, 16, 4, 1),
            optic, optic.Wavelengths[0]);
        for (var index = 0; index < mtf.Frequency.Count; index++)
        {
            var expected = Complex.FromPolarCoordinates(1, -2 * Math.PI * index * displacement / 16);
            Assert.InRange((mtf.TangentialOtf![index] - expected).Magnitude, 0, 1e-12);
            Assert.InRange((mtf.SagittalOtf![index] - Complex.One).Magnitude, 0, 1e-12);
        }
    }

    [Fact]
    public void ThroughFocusMtfMagnitudeMatchesItsComplexComponents()
    {
        var optic = Optic.CreateCookeTriplet();
        var focus = new[] { -0.01, 0.0, 0.01 };
        var settings = new MtfComputationSettings(PupilSampling: 8, ImageSize: 16);
        (double[] Tangential, double[] Sagittal) Evaluate(FftMtfDataType type) =>
            MtfMethodEvaluator.EvaluateFourierThroughFocus(optic, (0, 1), optic.Wavelengths,
                focus, 20, settings, type);
        var modulation = Evaluate(FftMtfDataType.Modulation);
        var real = Evaluate(FftMtfDataType.Real);
        var imaginary = Evaluate(FftMtfDataType.Imaginary);
        for (var index = 0; index < focus.Length; index++)
        {
            Assert.Equal(new Complex(real.Tangential[index], imaginary.Tangential[index]).Magnitude,
                modulation.Tangential[index], 12);
            Assert.Equal(new Complex(real.Sagittal[index], imaginary.Sagittal[index]).Magnitude,
                modulation.Sagittal[index], 12);
        }
    }

    [Fact]
    public void EmptySeidelDiagramPreservesUnavailableOutcome()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Wavelengths.Clear();
        var data = new SeidelDiagramAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(data.OutcomeReason));
    }

    [Fact]
    public void OppositeOtfPhasesCancelBeforeTakingModulation()
    {
        var frequency = new[] { 0.0, 1.0 };
        var modulation = new[] { 1.0, 1.0 };
        MtfResult Mono(double phase) => new(frequency, modulation, modulation, 2,
            new[] { Complex.One, new Complex(phase, 0) }, new[] { Complex.One, new Complex(phase, 0) });
        var result = MtfMethodEvaluator.CombinePolychromatic(new[]
        {
            (new Wavelength { Nanometers = 500, Weight = 1, IsPrimary = true }, Mono(1)),
            (new Wavelength { Nanometers = 600, Weight = 1 }, Mono(-1))
        });
        Assert.Equal(1, result.Tangential[0]);
        Assert.Equal(0, result.Tangential[1], 12);
        Assert.Equal(result.TangentialOtf![1].Magnitude, result.Tangential[1], 12);
        Assert.Equal(result.SagittalOtf![1].Magnitude, result.Sagittal[1], 12);
    }

    [Fact]
    public void GeometricOtfPreservesImageDisplacementPhase()
    {
        var frequency = new[] { 0.0, 1.0 };
        var a = GeometricMtfAnalysis.ComputeOtf(new[] { 0.0 }, new[] { 1.0 }, frequency, new[] { 1.0, 1.0 });
        var b = GeometricMtfAnalysis.ComputeOtf(new[] { 0.5 }, new[] { 1.0 }, frequency, new[] { 1.0, 1.0 });
        Assert.InRange(((a[1] + b[1]) / 2).Magnitude, 0, 1e-12);
        Assert.Equal(1, b[1].Magnitude, 12);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FloatingStopEntrancePupilTracesToTheDeclaredStopEdge(bool infinite)
    {
        var optic = Optic.CreateCookeTriplet();
        if (!infinite) { optic.SurfaceGroup.Items[0].Thickness = 500; optic.SurfaceGroup.Renumber(); }
        optic.Aperture.Kind = ApertureKind.FloatByStopSize;
        var stopIndex = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
        var stop = optic.SurfaceGroup.Items[stopIndex];
        var trace = optic.Paraxial.MarginalRay(optic.Wavelengths.First(wave => wave.IsPrimary).Micrometers);
        Assert.Equal(stop.SemiDiameter, Math.Abs(trace.Heights[stopIndex][0]), 10);
        if (infinite) Assert.Equal(12.102557103672, optic.Paraxial.EstimateEntrancePupilDiameter(), 9);
    }

    [Fact]
    public void ZeroWeightWavelengthsDoNotChangePolychromaticSpotStatistics()
    {
        var optic = Baseline();
        foreach (var wavelength in optic.Wavelengths) wavelength.Weight = wavelength.IsPrimary ? 1 : 0;
        var primary = optic.Wavelengths.ToList().FindIndex(wave => wave.IsPrimary) + 1;
        var all = SpotMetricEvaluator.Evaluate(optic, fieldNumber: 5);
        var mono = SpotMetricEvaluator.Evaluate(optic, wavelengthNumber: primary, fieldNumber: 5);
        Assert.Equal(mono.RmsSpotRadius, all.RmsSpotRadius, 12);
        Assert.Equal(mono.Radius80, all.Radius80, 12);
        Assert.Equal(mono.MaximumSpotRadius, all.MaximumSpotRadius, 12);
    }

    [Fact]
    public void PolychromaticCentroidUsesSpectralAndRayIntensityWeights()
    {
        var optic = Baseline();
        optic.Wavelengths[0].Weight = 7;
        optic.Wavelengths[1].Weight = 2;
        optic.Wavelengths[2].Weight = 1;
        var fields = SpotAnalysisEngine.DefinedFields(optic).TakeLast(1);
        var centered = SpotAnalysisEngine.Generate(optic, fields, optic.Wavelengths, 6, "hexapolar");
        var centroid = SpotAnalysisEngine.Centroid(centered.Fields[0].WeightedRays);
        Assert.InRange(Math.Abs(centroid.X), 0, 1e-12);
        Assert.InRange(Math.Abs(centroid.Y), 0, 1e-12);
        var unequal = SpotAnalysisEngine.Centroid(new[] { new SpotRayData(0, 0, 1), new SpotRayData(4, 8, 3) });
        Assert.Equal((3.0, 6.0), unequal);
    }

    [Fact]
    public void SpotVertexCoordinatesFollowImageTranslationAndRotation()
    {
        var optic = Optic.CreateCookeTriplet();
        SpotRayData Point() => SpotAnalysisEngine.Generate(optic, new[] { (0.0, 1.0) },
            new[] { optic.Wavelengths[0] }, 4, "hexapolar", reference: "absolute").Fields[0].Wavelengths[0].Rays[5];
        var before = Point();
        var image = optic.SurfaceGroup.Items[^1];
        var original = image.CoordinateSystem;
        image.CoordinateSystem = original with { Origin = original.Origin + new Vector3D(1, 2, 0) };
        var translated = Point();
        Assert.Equal(before.X - 1, translated.X, 11);
        Assert.Equal(before.Y - 2, translated.Y, 11);
        image.CoordinateSystem = original with { RotationZDegrees = 90 };
        var rotated = Point();
        Assert.Equal(before.Y, rotated.X, 11);
        Assert.Equal(-before.X, rotated.Y, 11);
    }

    [Fact]
    public void RotatingTheFieldToXPreservesRotationallySymmetricSeidelCoefficients()
    {
        var optic = Optic.CreateCookeTriplet();
        var expected = new SeidelCoefficientsAnalysis(optic).GenerateData().Table!;
        foreach (var field in optic.Fields) { field.X = field.Y; field.Y = 0; }
        var actual = new SeidelCoefficientsAnalysis(optic).GenerateData().Table!;
        Assert.Equal(expected.Rows.SelectMany(row => row), actual.Rows.SelectMany(row => row));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MtfFieldCoordinatesPublishPhysicalOrNormalizedUnitsAndIncrease(bool normalized)
    {
        var optic = Baseline();
        var data = new MtfVsFieldAnalysis(optic, MtfComputationMethod.Geometric,
            spatialFrequency: 20, fieldPointCount: 2,
            settings: new MtfComputationSettings(GeometricRayCount: 4),
            zemaxCompatibleOutput: normalized).GenerateData();
        foreach (var series in data.PlotSeries)
        {
            Assert.Equal(normalized ? AnalysisAxisQuantity.NormalizedField : AnalysisAxisQuantity.FieldHeight, series.XQuantity);
            Assert.Equal(normalized ? AnalysisAxisUnit.Dimensionless : AnalysisAxisUnit.Millimeter, series.XUnit);
            Assert.Equal(normalized ? 1 : 4.5, series.Points[^1].X, 12);
            Assert.True(series.Points.Zip(series.Points.Skip(1), (a, b) => b.X > a.X).All(value => value));
            var dto = WorkbenchMapper.ToSeriesDto(series);
            Assert.Equal(series.XQuantity.ToString(), dto.XQuantity.ToString());
        }
    }

    [Theory]
    [InlineData("Non-Sequential Ray Trace", AnalysisOutcome.NotApplicable)]
    [InlineData("Non-Sequential Detector Viewer", AnalysisOutcome.NotApplicable)]
    [InlineData("Incoherent Irradiance", AnalysisOutcome.Unavailable)]
    public void StatusPagesKeepTypedOutcomeAcrossRuntimeAndDto(string name, AnalysisOutcome expected)
    {
        var runtime = new WorkbenchRuntime(Baseline());
        var result = runtime.BuildAnalysisView(name);
        Assert.Equal(expected, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.OutcomeReason));
        var dto = WorkbenchMapper.ToAnalysisViewDto(result);
        Assert.Equal(expected.ToString(), dto.Outcome.ToString());
        Assert.Equal(result.OutcomeReason, dto.OutcomeReason);
    }

    private static Optic Baseline() => OpticalFormatCatalog.Import(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");
}
