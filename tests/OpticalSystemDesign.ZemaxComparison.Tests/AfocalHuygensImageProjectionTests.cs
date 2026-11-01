using System.Reflection;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Domain;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalHuygensImageProjectionTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(.12, -.09)]
    [InlineData(-.2, .15)]
    public void ObliquePropagationUsesDirectionCosineConstraintAndWindowRelativeCell(double centerX, double centerY)
    {
        // Independent two-wave interference: sz=sqrt(1-sx²-sy²), hence
        // dsz/dsx=-sx/sz and dsz/dsy=-sy/sz. Include nonzero pupil Z.
        var chief = new Vector3D(.1, -.3, Math.Sqrt(.9));
        var samples = new[]
        {
            new WavefrontSample(-.6, 0, -1.5, .2, .7, .08, 1),
            new WavefrontSample(.5, .3, 1.8, .6, -.4, -.03, .25)
        };
        var wavefront = new WavefrontResult(samples, double.PositiveInfinity, 0, 0,
            ImageSpaceAfocal: true, AfocalPupilDiameterMillimeters: 4)
        { AfocalReferencePlaneOrigin = Vector3D.Zero, AfocalChiefImageDirection = chief };
        var result = (double[,])typeof(DiffractionEngine)
            .GetMethod("HuygensFarFieldSummation", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [wavefront, new Wavelength { Micrometers = .55 }, 8, .03,
                CoordinateSystem.Global, 8, false, null, 0d, (centerX, centerY), false])!;
        var dx = -3.3 - chief.X / chief.Z * 1.1;
        var dy = -.4 - chief.Y / chief.Z * 1.1;
        var halfExtent = .61 * .55 * 6 / 4;
        for (var row = 0; row < 8; row++) for (var col = 0; col < 8; col++)
        {
            var cellX = (col - 4) * .03; var cellY = (row - 4) * .03;
            var phase = -2 * Math.PI * .11 - 2 * Math.PI / .55 * (dx * (cellX + centerX) + dy * (cellY + centerY));
            var footprint = Sinc(Math.PI * cellX / halfExtent) * Sinc(Math.PI * cellY / halfExtent);
            var expected = (1.25 + Math.Cos(phase)) * footprint * footprint;
            Assert.InRange(Math.Abs(result[row, col] - expected), 0, 3e-13);
        }
    }
    private static double Sinc(double value) => value == 0 ? 1 : Math.Sin(value) / value;
}
