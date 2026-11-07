using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class ContactPupilHuygensMtfTests
{
    [Theory]
    [InlineData("Miscellaneous/Grid Sag Example.zmx", 0, .7573484155220129, .5242604617232485)]
    [InlineData("Miscellaneous/Grid Sag Example.zmx", 1, .7573484155220129, .5242604617232485)]
    [InlineData("Polarization/Etalon.ZMX", 0, .757348414286066, .5242604592864025)]
    public async Task FrequencyAndFocusScansUseTheFiniteContactPupilWindow(
        string relativePath, int configuration, double mtfAt50, double throughFocusAt50)
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(),
            "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples", relativePath);
        var optic = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(source)).Configurations[configuration];
        var mtf = new HuygensMtfAnalysis(optic, numRays: 32, imageSize: 32, pixelPitchMillimeters: .00025,
            maximumFrequency: 50, wavelengthNumber: 1, fieldNumber: 1, zemaxCompatible: true).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, mtf.Outcome);
        Assert.Equal(2, mtf.PlotSeries.Count);
        foreach (var curve in mtf.PlotSeries)
        {
            Assert.All(curve.Points, point => Assert.InRange(point.Y, 0, 1));
            Assert.Equal(1, curve.Points[0].Y, 8);
            Assert.Equal(50, curve.Points[^1].X, 10);
            Assert.Equal(mtfAt50, curve.Points[^1].Y, 7);
        }

        var previous = optic.SurfaceGroup.Items[^2];
        var image = optic.SurfaceGroup.Items[^1];
        var thickness = previous.Thickness;
        var frame = image.CoordinateSystem;
        var settings = new MtfComputationSettings(PupilSampling: 32, ImageSize: 32,
            PixelPitchMillimeters: .00025, ZemaxCompatible: true, UseZemaxHuygensSemantics: true);
        var throughFocus = new MtfThroughFocusAnalysis(optic, MtfComputationMethod.Huygens,
            spatialFrequency: 50, deltaFocus: .01, focusPlaneCount: 5, settings: settings,
            wavelengthNumber: 1, fieldNumber: 1).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, throughFocus.Outcome);
        Assert.Equal(2, throughFocus.PlotSeries.Count);
        foreach (var curve in throughFocus.PlotSeries)
        {
            Assert.Equal(-.01, curve.Points[0].X, 10);
            Assert.Equal(.01, curve.Points[^1].X, 10);
            Assert.All(curve.Points, point => Assert.Equal(throughFocusAt50, point.Y, 7));
        }
        Assert.Equal(thickness, previous.Thickness);
        Assert.Equal(frame, image.CoordinateSystem);
    }
}
