using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class DiffractionEnergyRecoveryTests
{
    private static readonly Lazy<Optic> Source = new(() => OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(
        ProductDependencyArchitectureTests.RepositoryRoot(), "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx")), ".zmx"));

    [Theory]
    [InlineData("centroid", "encircled", false)]
    [InlineData("chief", "ensquared", false)]
    [InlineData("vertex", "encircled", false)]
    [InlineData("centroid", "encircled", true)]
    public void BoundedSpectralWindowMatchesIndependentFullImageMixture(string reference, string type, bool ideal)
    {
        var optic = Source.Value.Clone(); var field = SpotAnalysisEngine.DefinedFields(optic)[reference == "vertex" ? 0 : 2];
        var waves = optic.Wavelengths.ToArray(); const int n = 256, size = 512; const double maximum = 5;
        var spectrum = DiffractionEngine.ComputeFftCumulativeEnergySpectrum(optic, field, waves, n, ideal);
        var pitch = spectrum[0].Result.SampleSpacingMicrometers;
        var weights = waves.Select(w => w.Weight * Math.Pow(waves.Min(v => v.Micrometers) / w.Micrometers, 2)).ToArray();
        var samples = new EnergySample[size * size];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            samples[y * size + x] = new((x - size / 2d + 1) * pitch, (y - size / 2d + 1) * pitch,
                spectrum.Select((p, i) => weights[i] * p.Result.Values[(y + 1) % size, (x + 1) % size]).Sum());
        var total = samples.Sum(p => p.Weight); var center = (X: 0d, Y: 0d);
        if (reference == "centroid") center = (samples.Sum(p => p.X * p.Weight) / total, samples.Sum(p => p.Y * p.Weight) / total);
        if (reference == "vertex")
        {
            var primary = Array.FindIndex(waves, w => w.IsPrimary) + 1;
            var chief = RayBundleMetrics.Trace(optic, optic.SurfaceGroup.Items[^1].Number, primary, field.Hx, field.Hy,
                [new OpticalSystemDesign.Core.Raytrace.PupilSample(0, 0, 1)], requireUnvignetted: true,
                includeSurfaceTransmission: false, aimAtStop: spectrum[0].Result.UseRayAiming)[0];
            center = (-chief.Position.X * 1000, -chief.Position.Y * 1000);
        }
        var full = new FftCumulativeEnergyGrid(samples, center, type);
        var bounded = DiffractionEngine.ComputeBoundedSpectralCumulativeEnergy(optic, field, waves, n, reference, type, maximum, ideal);
        Assert.Equal(pitch, bounded.ImagePitch); Assert.Equal(n, bounded.FourierSampling);
        for (var i = 0; i < n; i++)
        {
            var r = maximum * i / (n - 1d);
            Assert.InRange(Math.Abs(full.Fraction(r) - bounded.Fraction(r)), 0, 1e-10);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => bounded.Fraction(maximum + .1));
    }

    [Fact]
    public void DefaultAllFieldPolychromaticEnergyRecoversWithoutChangingTheLensOrPhysicalInterval()
    {
        var optic = Source.Value.Clone(); var before = Snapshot(optic);
        var runtime = new WorkbenchRuntime(optic);
        var settings = runtime.MergeAnalysisSettings("Diffraction Encircled Energy", null);
        Assert.Equal("按需加密", settings["FourierSamplingMode"]); Assert.Equal("2048", settings["FourierMaximumSampling"]);
        var recovered = runtime.BuildAnalysisData("Diffraction Encircled Energy", settings);
        Assert.Equal(AnalysisOutcome.Success, recovered.Outcome);
        var report = Assert.IsType<FourierValidationReport>(recovered.Values["FourierSamplingValidation"]);
        Assert.True(report.Validated); Assert.Equal(64, report.RequestedPupilSampling); Assert.Equal(2048, report.ReturnedPupilSampling);
        Assert.Equal(new[] { 64, 128, 256, 512, 1024, 2048 }, report.Steps.Select(s => s.PupilSampling));
        Assert.All(report.Steps.Take(2), s => Assert.False(s.PhaseResolved));
        Assert.All(report.Steps.Skip(2), s => Assert.True(s.PhaseResolved));
        Assert.InRange(report.Steps[^1].RelativeRmsChange!.Value, 0, .001);
        Assert.InRange(report.EvaluatedNominalPupilCells, 1, report.MaximumNominalPupilCells);
        Assert.Equal(67_092_480, report.EvaluatedNominalPupilCells);
        Assert.Equal(4, recovered.PlotSeries.Count); Assert.Equal(20d, recovered.Values["MaximumDistanceMicrometers"]);
        foreach (var series in recovered.PlotSeries)
        {
            Assert.Equal(AnalysisAxisQuantity.Radius, series.XQuantity); Assert.Equal(AnalysisAxisUnit.Micrometer, series.XUnit);
            Assert.Equal(AnalysisAxisQuantity.EnergyFraction, series.YQuantity); Assert.Equal(401, series.Points.Count);
            Assert.Equal(0, series.Points[0].X); Assert.Equal(20, series.Points[^1].X);
            Assert.All(series.Points, p => Assert.InRange(p.Y, 0, 1));
            Assert.All(series.Points.Zip(series.Points.Skip(1)), p => Assert.True(p.Second.Y + 1e-12 >= p.First.Y));
        }
        Assert.Equal(FftPhaseSamplingStatus.UndersampledPhase, Assert.IsType<FftPupilSampling>(recovered.Values["RequestedPupilPhaseSampling"]).Status);
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData("Fixed")]
    [InlineData("ValidateRequested")]
    [InlineData("Automatic")]
    public void ExplicitFixedOrInsufficientBudgetCannotPublishUnresolvedCurves(string mode)
    {
        var runtime = new WorkbenchRuntime(Source.Value.Clone());
        var settings = runtime.MergeAnalysisSettings("Diffraction Encircled Energy", new Dictionary<string, string>
        { ["FourierSamplingMode"] = mode, ["FourierMaximumSampling"] = "64" });
        var data = runtime.BuildAnalysisData("Diffraction Encircled Energy", settings);
        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome); Assert.Empty(data.PlotSeries);
        if (mode == "ValidateRequested") Assert.False(Assert.IsType<FourierValidationReport>(data.Values["FourierSamplingValidation"]).Validated);
        else Assert.False(data.Values.ContainsKey("FourierSamplingValidation"));
    }

    [Fact]
    public void PhaseResolvedFixedGridIsReturnedUnchangedWithoutExtraRefinement()
    {
        var runtime = new WorkbenchRuntime(Source.Value.Clone());
        var settings = runtime.MergeAnalysisSettings("Diffraction Encircled Energy", new Dictionary<string, string> { ["PupilSampling"] = "256" });
        var automatic = runtime.BuildAnalysisData("Diffraction Encircled Energy", settings);
        var fixedData = runtime.BuildAnalysisData("Diffraction Encircled Energy", withMode("Fixed"));
        Assert.Equal(AnalysisOutcome.Success, automatic.Outcome); Assert.False(automatic.Values.ContainsKey("FourierSamplingValidation"));
        Assert.Equal(fixedData.PlotSeries.SelectMany(s => s.Points), automatic.PlotSeries.SelectMany(s => s.Points));
        Dictionary<string, string> withMode(string mode) => new(settings) { ["FourierSamplingMode"] = mode };
    }

    [Fact]
    public void CaptionUsesTypedPhysicalDataWithoutExposingInternalDiagnostics()
    {
        var runtime = new WorkbenchRuntime(Source.Value.Clone());
        var view = runtime.BuildAnalysisView("Diffraction Encircled Energy", new Dictionary<string, string>
        { ["PupilSampling"] = "256", ["FourierSamplingMode"] = "Fixed", ["MaximumDistanceMicrometers"] = "20" });
        var summary = Assert.IsType<OpticalSystemDesign.Application.Contracts.DiffractionEnergySummaryDto>(view.DiffractionEnergySummary);
        Assert.Equal(256, summary.ActualPupilSampling); Assert.Equal(256, summary.RequestedPupilSampling);
        Assert.Equal(3, summary.FieldCount); Assert.Equal(3, summary.WavelengthCount); Assert.Equal(20, summary.MaximumDistance);
        Assert.Equal(OpticalSystemDesign.Application.Contracts.AnalysisAxisUnit.Micrometer, summary.DistanceUnit);
        Assert.False(summary.SamplingValidated);
    }

    [Fact]
    public void CancellationBeforeRefinementLeavesTheSourceUnchanged()
    {
        var optic = Source.Value.Clone(); var before = Snapshot(optic); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new WorkbenchRuntime(optic).BuildAnalysisData("Diffraction Encircled Energy",
            new Dictionary<string, string> { ["FourierSamplingMode"] = "Automatic" }, cancelled.Token));
        Assert.Equal(before, Snapshot(optic));
    }

    private static string Snapshot(Optic optic) => JsonSerializer.Serialize(optic.ToSnapshot(), new JsonSerializerOptions
    { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
}
