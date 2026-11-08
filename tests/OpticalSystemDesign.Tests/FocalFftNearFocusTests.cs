using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class FocalFftNearFocusTests
{
    [Theory]
    [InlineData("Miscellaneous/Axicon model.zmx")]
    [InlineData("Miscellaneous/Toroid.zmx")]
    public void AxialFftEntryPointsReportTheMeasuredNearFocusFailure(string fixture)
    {
        var runtime = new WorkbenchRuntime(Import(fixture));
        var settings = new Dictionary<string, string>
        {
            ["Sampling"] = "64", ["PupilSampling"] = "32", ["Display"] = "128",
            ["WavelengthNumber"] = "1", ["FieldNumber"] = "1", ["ImageDeltaMicrometers"] = "0.25",
            ["MaximumDistanceMicrometers"] = "5", ["MaximumFrequency"] = "50"
        };
        foreach (var mode in new[] { "Fixed", "Adaptive", "Automatic" })
        {
            settings["FourierSamplingMode"] = mode;
            foreach (var name in new[] { "PSF", "MTF", "FFT PSF Cross Section", "Diffraction Encircled Energy" })
            {
                if (mode == "Automatic" && name == "Diffraction Encircled Energy") continue;
                var data = runtime.BuildAnalysisData(name, settings);
                Assert.Equal(AnalysisOutcome.NotApplicable, data.Outcome);
                Assert.Equal(FocalImageDomain.OutsideFftNearFocus, data.Values[FocalImageDomain.ResultKey]);
                Assert.Empty(data.PlotSeries);
                var witness = Assert.IsType<FftNearFocusEvidence>(data.Values[FocalImageDomain.FftNearFocusEvidenceKey]);
                Assert.True(witness.InnerMinimumTransverseMissWaves >= 10_000);
                Assert.True(witness.OuterMinimumTransverseMissWaves >= 10_000);
                Assert.True(witness.InnerMinimumSphericalCorrectionWaves >= 100);
                Assert.True(witness.OuterMinimumSphericalCorrectionWaves >= 100);
            }
        }
    }

    [Theory]
    [InlineData("Optical testing/Twyman Green Interferometer.zmx")]
    [InlineData("Miscellaneous/Irregular surface.ZMX")]
    [InlineData("Miscellaneous/Diode source simulation.zmx")]
    public void ATransportCorrectionAloneDoesNotExcludeVerifiedFftExamples(string fixture)
    {
        var optic = Import(fixture);
        Assert.Null(FocalImageDomain.CheckFftNearFocus("PSF", optic, (0, 0), optic.Wavelengths[0]));
    }

    [Theory]
    [InlineData("Miscellaneous/Cylinder.zmx")]
    [InlineData("Miscellaneous/MEMS DLL Sample.ZMX")]
    public void FocalFftRequiresSagittalAngularSupport(string fixture)
    {
        var runtime = new WorkbenchRuntime(Import(fixture));
        var settings = new Dictionary<string, string>
        {
            ["Sampling"] = "64", ["PupilSampling"] = "32", ["Display"] = "128", ["WavelengthNumber"] = "1",
            ["FieldNumber"] = "1", ["ImageDeltaMicrometers"] = "0.25", ["MaximumDistanceMicrometers"] = "5",
            ["MaximumFrequency"] = "50", ["FourierSamplingMode"] = "Fixed"
        };
        foreach (var name in new[] { "PSF", "MTF", "FFT PSF Cross Section", "Diffraction Encircled Energy" })
        {
            var data = runtime.BuildAnalysisData(name, settings);
            Assert.Equal(AnalysisOutcome.NotApplicable, data.Outcome);
            Assert.Equal(FocalImageDomain.NoSagittalDiffractionScale, data.Values[FocalImageDomain.ResultKey]);
            Assert.Empty(data.PlotSeries);
        }
    }

    private static Optic Import(string filename) => OpticalFormatCatalog.Import(
        File.ReadAllText(Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "src",
            "OpticalSystemDesign.App", "Assets", "SequentialExamples", filename)), ".zmx");
}
