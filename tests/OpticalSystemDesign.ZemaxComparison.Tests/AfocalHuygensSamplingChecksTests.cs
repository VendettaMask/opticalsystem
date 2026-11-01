using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apodization;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalHuygensSamplingChecksTests(ITestOutputHelper output)
{
    private static Optic Disk() => OpticalFormatCatalog.Import(
        "MODE SEQ\nUNIT MM X W X CM MR CPMM\nENPD 2\nFTYP 0 0 1 1 0 0 1\nWAVM 1 .55 1\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDIAM 1\nDISZ 10\nSURF 2\nDIAM 20", ".zmx");

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void FormerWideWindowFailuresRefineWithinTheOriginalStrictContinuumGate(int initial)
    {
        var optic = Disk();
        var psf = DiffractionEngine.ComputeCheckedAfocalHuygensPsf(optic, (0, 0), optic.Wavelengths[0], initial, 32, .08);
        var check = Assert.Single(psf.HuygensSamplingChecks!);
        Assert.Equal("SamplingStable", check.Status); Assert.Equal(initial, psf.PupilSampling);
        Assert.False(check.IsContinuumErrorBound);
        Assert.Equal("PsfQuadratureOnFixedReportedWindow", check.Scope);
        Assert.True(psf.PupilQuadratureNodeCount > initial * initial);
        Assert.InRange(check.CumulativeOperations, 1, AnalysisResourceLimits.MaximumDirectPsfOperations);
        Assert.InRange(check.MaximumIntensityDisagreement!.Value, 0, 1e-10);
        Assert.InRange(check.MaximumReferenceAmplitudeDisagreement!.Value, 0, 1e-10);
        var maximum = 0d;
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            var z = Math.PI * 2 * .08 * Math.Sqrt((x - 16) * (x - 16) + (y - 16) * (y - 16)) / .55;
            maximum = Math.Max(maximum, Math.Abs(psf.Values[y, x] / 100 - Airy(z)));
        }
        Assert.InRange(maximum, 0, 1e-10);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "checked-wide-window", initial, maximum, check }));
    }

    [Theory]
    [InlineData(.2)]
    [InlineData(3.7)]
    [InlineData(8.8)]
    public void RapidRadialPhaseUsesIndependentRefinementAndTheAnalyticComplexIntegral(double defocus)
    {
        var optic = Disk(); var phase = Math.PI * defocus / .55;
        var expected = Math.Pow(Math.Sin(phase / 2) / (phase / 2), 2);
        var psf = DiffractionEngine.ComputeCheckedAfocalHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 8, 1, .02, defocus: defocus);
        Assert.InRange(Math.Abs(psf.Values[0, 0] / 100 - expected), 0, 1e-10);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "checked-radial-phase", defocus, expected, actual = psf.Values[0, 0] / 100, checks = psf.HuygensSamplingChecks }));
    }

    [Fact]
    public void ReferenceAmplitudeCheckDetectsAngularAliasingHiddenByPeakNormalization()
    {
        var optic = Disk(); optic.Apodization = new AngularPower();
        var psf = DiffractionEngine.ComputeCheckedAfocalHuygensPsf(optic, (0, 0), optic.Wavelengths[0], 8, 1, .02);
        var check = Assert.Single(psf.HuygensSamplingChecks!);
        // All normalized central intensities equal one. The passive pupil's
        // true mean field is .7, while the initial 16-arm rule aliases it as 1.
        Assert.InRange(Math.Abs(psf.HuygensReferenceAmplitude!.Value - .7), 0, 1e-12);
        Assert.Contains(check.Trials, trial => trial.ReferenceAmplitude > .9);
        Assert.InRange(Math.Abs(psf.Values[0, 0] / 100 - 1), 0, 1e-12);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "checked-angular-alias", psf.HuygensReferenceAmplitude, check }));
    }

    [Fact]
    public void InsufficientBudgetRejectsBeforeAllocatingTheFirstRule()
    {
        var optic = Disk();
        var error = Assert.Throws<AnalysisDataUnavailableException>(() => DiffractionEngine.ComputeCheckedAfocalHuygensPsf(
            optic, (0, 0), optic.Wavelengths[0], 8, 32, .08, new(MaximumOperations: 100)));
        var check = Assert.IsType<HuygensSamplingDiagnostics>(error.Data["HuygensSamplingDiagnostics"]);
        Assert.Equal("WorkLimitReached", check.Status); Assert.Empty(check.Trials);
        Assert.Equal(0, check.CumulativeOperations); Assert.Null(check.MaximumIntensityDisagreement);
    }

    [Theory]
    [InlineData("Huygens PSF")]
    [InlineData("Huygens PSF Cross Section")]
    [InlineData("Huygens MTF")]
    public void ResolvedCircularBoundaryFlowsThroughAllThreeApplicationPaths(string name)
    {
        var optic = Disk(); optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.73);
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(name, Settings());
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal("CheckedGaussianPupil", data.Values["AfocalIntegration"]);
        var checks = Assert.IsAssignableFrom<IReadOnlyList<HuygensSamplingDiagnostics>>(data.Values["SamplingChecks"]);
        Assert.Equal("SamplingStable", checks[^1].Status);
        Assert.Equal("PlanarApertureBoundaries", checks[^1].PupilPartition);
        Assert.True(checks.Sum(check => check.CumulativeOperations) <= AnalysisResourceLimits.MaximumDirectPsfOperations);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "checked-clipping-resolved", name, checks }));
    }

    [Theory]
    [InlineData("Huygens PSF")]
    [InlineData("Huygens PSF Cross Section")]
    [InlineData("Huygens MTF")]
    public void AllThreeApplicationPathsRetainActualSamplingDiagnostics(string name)
    {
        var data = new WorkbenchRuntime(Disk()).BuildAnalysisData(name, Settings());
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal("SamplingStable", data.Values["SamplingStatus"]);
        var checks = Assert.IsAssignableFrom<IReadOnlyList<HuygensSamplingDiagnostics>>(data.Values["SamplingChecks"]);
        Assert.NotEmpty(checks); Assert.All(checks, check => Assert.Equal(8, check.InitialSampling));
        Assert.True(checks.Sum(check => check.CumulativeOperations) <= AnalysisResourceLimits.MaximumDirectPsfOperations);
    }

    [Theory]
    [InlineData("Huygens PSF")]
    [InlineData("Huygens MTF")]
    public void SpectralComponentsShareOneCumulativeWorkBudget(string name)
    {
        var optic = Disk(); optic.Wavelengths.Add(new Wavelength { Micrometers = .65, Weight = 1, IsPrimary = false });
        var settings = Settings(); settings["PupilSampling"] = "64";
        settings["ImageSampling"] = "32"; settings["ImageDeltaMilliradians"] = ".02"; settings["WavelengthNumber"] = "0";
        var data = new WorkbenchRuntime(optic).BuildAnalysisData(name, settings);
        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome); Assert.Empty(data.PlotSeries);
        var checks = Assert.IsAssignableFrom<IReadOnlyList<HuygensSamplingDiagnostics>>(data.Values["SamplingChecks"]);
        Assert.True(checks.Count >= 2);
        Assert.Equal("WorkLimitReached", checks[^1].Status);
        Assert.True(checks.Sum(check => check.CumulativeOperations) <= AnalysisResourceLimits.MaximumDirectPsfOperations);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "checked-spectral-budget", name, checks }));
    }

    [Fact]
    public void MultipleMtfFieldsShareTheSameAnalysisWorkBudget()
    {
        var optic = Disk(); optic.Fields.Add(new FieldPoint { Y = 0 });
        var settings = Settings(); settings["PupilSampling"] = "64"; settings["ImageSampling"] = "32";
        settings["ImageDeltaMilliradians"] = ".02"; settings["FieldNumber"] = "0";
        var data = new WorkbenchRuntime(optic).BuildAnalysisData("Huygens MTF", settings);
        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome); Assert.Empty(data.PlotSeries);
        var checks = Assert.IsAssignableFrom<IReadOnlyList<HuygensSamplingDiagnostics>>(data.Values["SamplingChecks"]);
        Assert.True(checks.Count >= 2); Assert.Equal("WorkLimitReached", checks[^1].Status);
        Assert.True(checks.Sum(check => check.CumulativeOperations) <= AnalysisResourceLimits.MaximumDirectPsfOperations);
        output.WriteLine(JsonSerializer.Serialize(new { kind = "checked-field-budget", checks }));
    }

    private static Dictionary<string, string> Settings() => new()
    {
        ["AfocalIntegration"] = "收敛校验圆瞳积分", ["PupilSampling"] = "8", ["ImageSampling"] = "16",
        ["ImageDeltaMilliradians"] = ".08", ["WavelengthNumber"] = "1", ["FieldNumber"] = "1"
    };
    private sealed class AngularPower : IApodizationModel
    {
        public string Kind => "test-angular-field";
        public double Intensity(double x, double y) => Math.Pow(.7 + .3 * Math.Cos(16 * Math.Atan2(y, x)), 2);
        public IApodizationModel Clone() => new AngularPower();
    }
    private static double Airy(double z)
    {
        if (z == 0) return 1; var term = z / 2; var sum = term;
        for (var k = 1; k < 100; k++) { term *= -z * z / (4 * k * (k + 1)); sum += term; if (Math.Abs(term) < 1e-17) break; }
        return Math.Pow(2 * sum / z, 2);
    }
}
