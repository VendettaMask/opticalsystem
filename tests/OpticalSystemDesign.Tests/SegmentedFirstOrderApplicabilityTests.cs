using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Validation;

namespace OpticalSystemDesign.Tests;

public sealed class SegmentedFirstOrderApplicabilityTests
{
    [Fact]
    public void AxialMirrorStateBoundaryWithholdsTwoSidedCardinalParameters()
    {
        var optic = ReadExample();

        var result = new WorkbenchRuntime(optic).BuildAnalysisData("First Order", new Dictionary<string, string>());

        Assert.Equal(AnalysisOutcome.NotApplicable, result.Outcome);
        Assert.Equal(false, result.Values["CardinalParametersApplicable"]);
        Assert.False(result.Values.ContainsKey("EffectiveFocalLength"));
        Assert.False(result.Values.ContainsKey("FNumber"));
        var jump = Assert.IsType<double>(result.Values["AxialDirectionJumpDegrees"]);
        Assert.InRange(Math.Abs(jump - 17.298330210575084), 0, 1e-7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UniformOrInRowAxialMirrorKeepsTheFirstOrderReport(bool insideRow)
    {
        var optic = ReadExample();
        var surface = Assert.Single(optic.SurfaceGroup.Items, item => item.Geometry is TernaryMirrorArrayGeometry);
        var array = Assert.IsType<TernaryMirrorArrayGeometry>(surface.Geometry);
        if (insideRow)
        {
            // All three fixed-size probe pairs can still straddle a nearby line.
            // The actual axial ray lies in one row, so its limiting rays agree.
            var frame = surface.CoordinateSystem;
            surface.CoordinateSystem = frame with { Origin = frame.ToGlobalPoint(new(0, 1e-5, 0)) };
        }
        else
        {
            surface.Geometry = new TernaryMirrorArrayGeometry(array.PixelsX, array.PixelsY, array.WidthX, array.WidthY,
                array.TiltRadians, array.AzimuthRadians, array.AddressingMode, [0]);
        }

        var result = new FirstOrderAnalysis(optic).GenerateData();

        Assert.Equal(AnalysisOutcome.Success, result.Outcome);
        Assert.Equal(true, result.Values["CardinalParametersApplicable"]);
        Assert.Equal(double.PositiveInfinity, Assert.IsType<double>(result.Values["EffectiveFocalLength"]));
    }

    private static OpticalSystemDesign.Core.Optic ReadExample()
    {
        var source = Path.Combine(ProductDependencyArchitectureTests.RepositoryRoot(), "src", "OpticalSystemDesign.App",
            "Assets", "SequentialExamples", "Miscellaneous", "MEMS DLL Sample.ZMX");
        return OpticalFormatCatalog.Import(EvidenceFiles.ReadAllText(source), ".zmx");
    }
}
