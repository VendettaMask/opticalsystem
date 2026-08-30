using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxZernikeFringeTests
{
    [Fact]
    public void FringeFitUsesZemaxFixedThirtySevenTermTable()
    {
        var coefficients = ZernikeFitEngine.FitFringe(
            Array.Empty<WavefrontSample>(),
            numTerms: 128);

        var expectedIndices = new[]
        {
            (0, 0),
            (1, 1), (1, -1),
            (2, 0), (2, 2), (2, -2),
            (3, 1), (3, -1),
            (4, 0),
            (3, 3), (3, -3),
            (4, 2), (4, -2),
            (5, 1), (5, -1),
            (6, 0),
            (4, 4), (4, -4),
            (5, 3), (5, -3),
            (6, 2), (6, -2),
            (7, 1), (7, -1),
            (8, 0),
            (5, 5), (5, -5),
            (6, 4), (6, -4),
            (7, 3), (7, -3),
            (8, 2), (8, -2),
            (9, 1), (9, -1),
            (10, 0),
            (12, 0)
        };

        Assert.Equal(ZernikeFitEngine.MaximumFringeTerm, coefficients.Count);
        Assert.Equal(expectedIndices, coefficients.Select(coefficient =>
            (coefficient.RadialOrder, coefficient.AzimuthalOrder)));
        Assert.Equal((10, 0), (
            coefficients[35].RadialOrder,
            coefficients[35].AzimuthalOrder));
        Assert.Equal((12, 0), (
            coefficients[36].RadialOrder,
            coefficients[36].AzimuthalOrder));
        Assert.DoesNotContain(coefficients, coefficient =>
            coefficient.RadialOrder == 6 && Math.Abs(coefficient.AzimuthalOrder) == 6);
    }

    [Fact]
    public void FringeAnalysisUsesZemaxUniformGridAndReportsTwelfthOrderSphericalTerm()
    {
        var data = new ZernikeAnalysis(
            Optic.CreateCookeTriplet(),
            ZernikeAnalysisKind.ZemaxFringe,
            numRings: 32,
            numTerms: 37,
            mapSize: 17,
            wavelengthNumber: 2,
            fieldNumber: 1,
            name: "Zernike Fringe").GenerateData();

        Assert.Equal("32 x 32", data.Values["Sampling"]);
        Assert.Equal(740, Convert.ToInt32(data.Values["RayCount"]));
        Assert.Contains("Z  37", data.ReportText, StringComparison.Ordinal);
        Assert.Contains("924p^12 - 2772p^10", data.ReportText, StringComparison.Ordinal);
    }
}
