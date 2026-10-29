using System.Reflection;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class HuygensAfocalDefocusTests
{
    [Theory]
    [InlineData(false, -.2)]
    [InlineData(false, 0)]
    [InlineData(false, .2)]
    [InlineData(true, -.2)]
    [InlineData(true, 0)]
    [InlineData(true, .2)]
    public void DistortedPupilTwoEmitterInterferenceKeepsPhaseAndPropagationCoordinatesDistinct(bool paraxial, double defocus)
    {
        // Two emitters have deliberately different normalized and physical radii.
        // Closed-form two-beam interference distinguishes the two power datums.
        var samples = new[]
        {
            new WavefrontSample(-.6, 0, -1.5, .2, 0, .08, 1),
            new WavefrontSample(.5, .3, 1.8, .6, 0, -.03, .25)
        };
        var wavefront = new WavefrontResult(samples, double.PositiveInfinity, 0, 0,
            ImageSpaceAfocal: true, AfocalPupilDiameterMillimeters: 4)
            { AfocalReferencePlaneOrigin = Vector3D.Zero };
        var result = (double[,])typeof(DiffractionEngine)
            .GetMethod("HuygensFarFieldSummation", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [wavefront, new Wavelength { Micrometers = .55 }, 8, .03,
                CoordinateSystem.Global, 8, false, null, defocus, null, paraxial])!;
        var radiusSquaredDifference = paraxial
            ? 4 * (.36 - .25 - .09)
            : (2.25 + .04) - (3.24 + .36);
        var halfExtent = .61 * .55 * 6 / 4;
        foreach (var (row, col) in new[] { (4, 4), (2, 5), (6, 1), (0, 7) })
        {
            var x = (col - 4) * .03;
            var y = (row - 4) * .03;
            var phaseDifference = -2 * Math.PI * (.11 - defocus * radiusSquaredDifference / (2000 * .00055))
                - 2 * Math.PI / .00055 * ((-3.3 * x - .4 * y) / 1000);
            var footprint = Sinc(Math.PI * x / halfExtent) * Sinc(Math.PI * y / halfExtent);
            var expected = (1.25 + Math.Cos(phaseDifference)) * footprint * footprint;
            Assert.InRange(Math.Abs(result[row, col] - expected), 0, 2e-13);
        }
    }

    [Theory]
    [InlineData(-.01)]
    [InlineData(0)]
    [InlineData(.01)]
    public async Task PublicGeneralDefocusDefaultRemainsPhysicalAndZeroDefocusIsIdentical(double defocus)
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/afocal-repair/sources/Afocal/Beam Expander.ZMX"))).ActiveOptic;
        var wave = optic.Wavelengths[0];
        var original = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 8, 16, .01, defocus: defocus);
        var physical = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 8, 16, .01, defocus: defocus,
            useParaxialPupilDefocus: false);
        var paraxial = DiffractionEngine.ComputeHuygensPsf(optic, (0, 0), wave, 8, 16, .01, defocus: defocus,
            useParaxialPupilDefocus: true);
        Assert.Equal(original.Values.Cast<double>(), physical.Values.Cast<double>());
        if (defocus == 0)
            Assert.Equal(original.Values.Cast<double>(), paraxial.Values.Cast<double>());
        else
            Assert.True(original.Values.Cast<double>().Zip(paraxial.Values.Cast<double>(), (a, b) => Math.Abs(a - b)).Max() > 1e-4);
    }

    private static double Sinc(double value) => value == 0 ? 1 : Math.Sin(value) / value;
}
