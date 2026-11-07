using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class ObscuredRayFanNativeTests
{
    [Theory]
    [InlineData("Telescopes/Schmidt-Cassegrain spider obscuration.zmx", 1, 3, 29, 1.54247942760577)]
    [InlineData("Zoom systems/Mirror zoom.zmx", 3, 1, 38, 242.460968196504)]
    public async Task ObscuredChiefStillDefinesTheFrozenNativeFanOrigin(
        string relativeSource, int field, int wavelength, int ordinateIndex, double nativeMicrometers)
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(),
            "src/OpticalSystemDesign.App/Assets/SequentialExamples", relativeSource);
        var optic = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(source)).Configurations[0];
        var result = new RayFanAnalysis(optic, numberOfRaysEachSide: 20, fieldNumber: field,
            wavelengthNumber: wavelength, vignettedPupil: true, zemaxCompatible: true).GenerateData();
        var tangential = Assert.Single(result.PlotPanes![0].Series).Points;
        Assert.True(double.IsNaN(tangential[20].Y));
        Assert.InRange(Math.Abs(tangential[ordinateIndex].Y * 1000 - nativeMicrometers), 0, 1e-6);
    }
}
