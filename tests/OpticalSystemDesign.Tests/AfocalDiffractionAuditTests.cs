using System.Globalization;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class AfocalDiffractionAuditTests
{
    [Theory]
    [InlineData("Fourier Through Focus MTF", "Frequency")]
    [InlineData("Huygens Through Focus MTF", "SpatialFrequency")]
    [InlineData("Geometric Through Focus MTF", "SpatialFrequency")]
    public void AfocalScanPresetsTrackPhysicalAngularCutoff(string analysis, string key)
    {
        var optic = PlaneBeam(); var runtime = new WorkbenchRuntime(optic);
        var descriptor = runtime.GetAnalysisParameters(analysis).Single(p => p.Key == key);
        var expected = .1 * 2 / .55;
        Assert.Equal(expected, double.Parse(descriptor.DefaultValue, CultureInfo.InvariantCulture), 10);
        Assert.Contains("cycles/mrad", descriptor.DisplayName);
        Assert.Contains("D", runtime.GetAnalysisParameters(analysis).Single(p => p.Key == "DeltaFocus").DisplayName);
        optic.Aperture.Value = 4;
        var wider = runtime.GetAnalysisParameters(analysis).Single(p => p.Key == key);
        Assert.Equal(2 * expected, double.Parse(wider.DefaultValue, CultureInfo.InvariantCulture), 10);
    }

    [Theory]
    [InlineData("Fourier MTF vs Field")]
    [InlineData("Huygens MTF vs Field")]
    [InlineData("Geometric MTF vs Field")]
    public void AfocalFieldPresetsCoverTheBandWithoutChangingFocalPresets(string analysis)
    {
        var optic = PlaneBeam(); var runtime = new WorkbenchRuntime(optic);
        var parameters = runtime.GetAnalysisParameters(analysis);
        var frequencies = parameters.Where(p => p.Key.StartsWith("Frequency", StringComparison.Ordinal))
            .Select(p => double.Parse(p.DefaultValue, CultureInfo.InvariantCulture)).ToArray();
        var cutoff = 2 / .55;
        Assert.Equal(6, frequencies.Length);
        Assert.All(frequencies, value => Assert.InRange(value, .009 * cutoff, .501 * cutoff));
        optic.ImageSpaceAfocal = false;
        if (analysis == "Geometric MTF vs Field")
            Assert.Equal("20", runtime.GetAnalysisParameters(analysis).Single(p => p.Key == "SpatialFrequency").DefaultValue);
        else
            Assert.Equal(Enumerable.Range(1, 6).Select(i => i * 10d), runtime.GetAnalysisParameters(analysis)
                .Where(p => p.Key.StartsWith("Frequency", StringComparison.Ordinal))
                .Select(p => double.Parse(p.DefaultValue, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void SavedOutOfBandFrequenciesRemainExplicitRatherThanClippedToThePreset()
    {
        var runtime = new WorkbenchRuntime(PlaneBeam());
        var data = runtime.BuildAnalysisData("Geometric Through Focus MTF", new Dictionary<string, string>
        { ["SpatialFrequency"] = "50", ["Steps"] = "1", ["PupilSampling"] = "8", ["WavelengthNumber"] = "1", ["FieldNumber"] = "1" });
        Assert.Equal(50d, data.Values["SpatialFrequency"]);
        Assert.All(data.PlotSeries.SelectMany(s => s.Points), point => Assert.InRange(Math.Abs(point.Y), 0, 1e-12));
    }

    [Fact]
    public void ExplicitFourierZeroKeepsTheExistingFallbackAndIncompleteSettingsRemainReachable()
    {
        var runtime = new WorkbenchRuntime(PlaneBeam());
        var data = runtime.BuildAnalysisData("Fourier Through Focus MTF", new Dictionary<string, string>
        { ["Frequency"] = "0", ["NumberOfSteps"] = "1", ["Sampling"] = "8", ["WavelengthNumber"] = "1", ["FieldNumber"] = "1" });
        Assert.Equal(0d, data.Values["FrequencyInput"]);
        Assert.Equal(50d, data.Values["SpatialFrequency"]);
        var incomplete = PlaneBeam();
        var empty = new WorkbenchRuntime(incomplete);
        incomplete.Wavelengths.Clear();
        Assert.Equal("0", empty.GetAnalysisParameters("Huygens Through Focus MTF")
            .Single(p => p.Key == "SpatialFrequency").DefaultValue);
        Assert.NotEmpty(empty.GetAnalysisParameters("Fourier MTF vs Field"));
    }

    [Theory]
    [InlineData(FoucaultKnifeEdge.HorizontalAbove)]
    [InlineData(FoucaultKnifeEdge.HorizontalBelow)]
    [InlineData(FoucaultKnifeEdge.VerticalLeft)]
    [InlineData(FoucaultKnifeEdge.VerticalRight)]
    public void AngularKnifeHasPhysicalGridAndOpenClosedPowerLimits(FoucaultKnifeEdge knife)
    {
        var optic = PlaneBeam(); var wave = optic.Wavelengths.Single();
        var openSign = knife is FoucaultKnifeEdge.HorizontalAbove or FoucaultKnifeEdge.VerticalRight ? 1 : -1;
        var opened = FoucaultEngine.Compute(optic, (0, 0), wave, 16, knife, openSign * 1000);
        var closed = FoucaultEngine.Compute(optic, (0, 0), wave, 16, knife, -openSign * 1000);
        Assert.Equal(AnalysisAxisUnit.Milliradian, opened.ImageCoordinateUnit);
        // dx_angle = wavelength(mm)/(pupil diameter(mm)) * N/Nfft * 1000 mrad/rad.
        Assert.Equal(.55 / 2 * 16 / 64, opened.ImageSpacingXMilliradians, 12);
        Assert.Equal(1, opened.KnifeThroughput, 10);
        Assert.Equal(0, closed.KnifeThroughput, 12);
        Assert.All(opened.Intensity.Cast<double>(), value => Assert.True(double.IsFinite(value) && value >= 0));
        Assert.Equal(0, opened.ImageSpacingXMicrometers);
    }

    [Fact]
    public void AngularKnifeCoordinatesAndPupilDiameterScaleTogether()
    {
        var optic = PlaneBeam(); var wave = optic.Wavelengths.Single();
        var first = FoucaultEngine.Compute(optic, (0, 0), wave, 16, FoucaultKnifeEdge.HorizontalAbove, .08);
        optic.Aperture.Value = 4;
        var second = FoucaultEngine.Compute(optic, (0, 0), wave, 16, FoucaultKnifeEdge.HorizontalAbove, .04);
        Assert.Equal(first.KnifeThroughput, second.KnifeThroughput, 10);
        Assert.All(first.Intensity.Cast<double>().Zip(second.Intensity.Cast<double>()), pair =>
            Assert.Equal(pair.First, pair.Second, 10));
    }

    [Fact]
    public void RiflescopeAutomaticMtfValidatesEveryFieldAndWavelengthWithoutRelaxingPhaseGate()
    {
        var runtime = new WorkbenchRuntime(Riflescope());
        var fixedGrid = runtime.BuildAnalysisData("MTF", new Dictionary<string, string>
        { ["FourierSamplingMode"] = "Fixed", ["Sampling"] = "64" });
        Assert.Equal(AnalysisOutcome.Unavailable, fixedGrid.Outcome);
        var recovered = runtime.BuildAnalysisData("MTF", new Dictionary<string, string>
        { ["Sampling"] = "64", ["FourierMaximumSampling"] = "1024" });
        Assert.Equal(AnalysisOutcome.Success, recovered.Outcome);
        var validation = Assert.IsType<FourierValidationReport>(recovered.Values["FourierSamplingValidation"]);
        Assert.True(validation.Validated);
        Assert.Equal(.001, validation.RelativeRmsAccuracy);
        Assert.False(validation.Steps[0].PhaseResolved);
        Assert.All(validation.Steps.TakeLast(2), step => Assert.True(step.PhaseResolved));
        Assert.Equal(3, recovered.Values["FieldCount"]);
        Assert.Equal(3, Assert.IsType<double[]>(recovered.Values["WavelengthsMicrometers"]).Length);
        Assert.Equal(6, recovered.PlotSeries.Count);
        Assert.True(validation.RequestedRelativeRmsDeviation > validation.RelativeRmsAccuracy);
    }

    [Fact]
    public void RiflescopeKnifeSucceedsAndCarriesTypedAngularCaptionMetadata()
    {
        var optic = Riflescope(); var before = optic.ToSnapshot();
        var runtime = new WorkbenchRuntime(optic);
        var data = runtime.BuildAnalysisData("Foucault Analysis", new Dictionary<string, string>
        { ["Sampling"] = "32", ["WavelengthNumber"] = "2", ["FieldNumber"] = "1", ["YPositionMicrometers"] = "0.01" });
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(AnalysisAxisUnit.Milliradian, data.Values["KnifeCoordinateUnit"]);
        Assert.False(data.Values.ContainsKey("KnifePositionMicrometers"));
        var view = WorkbenchMapper.ToAnalysisViewDto(runtime.BuildAnalysisView("Foucault Analysis", new Dictionary<string, string>
        { ["Sampling"] = "16", ["YPositionMicrometers"] = "0.01" }));
        Assert.Equal(OpticalSystemDesign.Application.Contracts.AnalysisAxisUnit.Milliradian, view.FoucaultSummary!.KnifeUnit);
        Assert.Equal(.01, view.FoucaultSummary.KnifePosition);
        Assert.Equal(before.ImageSpaceAfocal, optic.ImageSpaceAfocal);
        Assert.Equal(before.Fields, optic.ToSnapshot().Fields);
    }

    internal static Optic Riflescope()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpticalSystemDesign.slnx"))) root = root.Parent;
        if (root is null) throw new DirectoryNotFoundException("Repository root");
        return OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(root.FullName,
            "src/OpticalSystemDesign.App/Assets/SequentialExamples/Afocal/Afocal Riflescope.ZMX")), ".zmx");
    }

    private static Optic PlaneBeam()
    {
        var optic = new Optic { ImageSpaceAfocal = true };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, SemiDiameter = 5, Thickness = 10 },
            new OpticalSurface { SemiDiameter = 5, Thickness = 0 }
        ]);
        return optic;
    }
}
