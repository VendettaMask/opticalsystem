using OptilandWorkbench.App.Manufacturing;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Tests;

public sealed class ZemaxMechanicalSemiDiameterTests
{
    private const string Source = """
        MODE SEQ
        NAME "Independent mechanical semi-diameters"
        UNIT MM
        ENPD 8
        WAVM 1 0.55 1
        PWAV 1
        SURF 0
          DISZ 20
          DIAM 0 0 0 0 1 ""
          MEMA 0 0 0 0 1 ""
        SURF 1
          CURV 0.02
          DISZ 4
          GLAS N-BK7
          DIAM 5 1 0 0 1 ""
          MEMA 6.75 1 0 0 1 ""
        SURF 2
          CURV -0.02
          DISZ 20
          DIAM 4.5 1 0 0 1 ""
          MEMA 7.25 2 0 0 1 ""
        SURF 3
          DISZ 0
          DIAM 2 0 0 0 1 ""
          MEMA 3.5 0 0 0 1 ""
        """;

    [Fact]
    public void ImportKeepsMemaIndependentFromDiamAcrossSnapshotAndZmx()
    {
        var optic = OpticalFormatCatalog.Import(Source, ".zmx");
        AssertValues(optic);

        var snapshot = optic.ToSnapshot();
        Assert.Equal(7.25, snapshot.Surfaces[2].MechanicalSemiDiameter);
        Assert.Equal(2, snapshot.Surfaces[2].MechanicalSemiDiameterSolveCode);
        AssertValues(Optic.FromSnapshot(snapshot));

        var exported = OpticalFormatCatalog.Export(optic, ".zmx");
        Assert.Contains("  MEMA 6.75 1 0 0 1 \"\"", exported, StringComparison.Ordinal);
        Assert.Contains("  MEMA 7.25 2 0 0 1 \"\"", exported, StringComparison.Ordinal);
        AssertValues(OpticalFormatCatalog.Import(exported, ".zmx"));
    }

    [Fact]
    public void EditorLayoutAndManufacturingUseImportedMechanicalSemiDiameter()
    {
        var optic = OpticalFormatCatalog.Import(Source, ".zmx");
        var rows = optic.SurfaceGroup.Items.Select(WorkbenchMapper.ToSurfaceDto).ToArray();
        Assert.Equal(6.75, rows[1].MechanicalSemiDiameter);
        Assert.Equal(7.25, rows[2].MechanicalSemiDiameter);

        var element = new OpticalElementDefinition(1, rows[1], rows[2]);
        Assert.Equal(14.5, element.MechanicalDiameter, precision: 12);

        var lens = Assert.Single(new Layout2DBuilder(optic).Build().LensElements);
        Assert.Equal(7.25, lens.Boundary.Max(point => Math.Abs(point.Y)), precision: 10);
    }

    [Fact]
    public void MissingMemaFallsBackToClearSemiDiameter()
    {
        var optic = OpticalFormatCatalog.Import(
            Source.Replace("MEMA 7.25 2 0 0 1 \"\"", "IGNORED", StringComparison.Ordinal),
            ".zmx");
        Assert.Equal(4.5, optic.SurfaceGroup.Items[2].MechanicalSemiDiameter);
        optic.SurfaceGroup.Items[2].SemiDiameter = 5.125;
        Assert.Equal(5.125, optic.SurfaceGroup.Items[2].MechanicalSemiDiameter);
        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        restored.SurfaceGroup.Items[2].SemiDiameter = 5.75;
        Assert.Equal(5.75, restored.SurfaceGroup.Items[2].MechanicalSemiDiameter);
    }

    [Fact]
    public void MemaUsesTheDocumentLengthUnit()
    {
        var optic = OpticalFormatCatalog.Import(
            Source.Replace("UNIT MM", "UNIT CM", StringComparison.Ordinal),
            ".zmx");
        Assert.Equal(50, optic.SurfaceGroup.Items[1].SemiDiameter);
        Assert.Equal(67.5, optic.SurfaceGroup.Items[1].MechanicalSemiDiameter);
        Assert.Equal(72.5, optic.SurfaceGroup.Items[2].MechanicalSemiDiameter);
    }

    private static void AssertValues(Optic optic)
    {
        Assert.Equal(5, optic.SurfaceGroup.Items[1].SemiDiameter);
        Assert.Equal(6.75, optic.SurfaceGroup.Items[1].MechanicalSemiDiameter);
        Assert.Equal(1, optic.SurfaceGroup.Items[1].MechanicalSemiDiameterSolveCode);
        Assert.Equal(4.5, optic.SurfaceGroup.Items[2].SemiDiameter);
        Assert.Equal(7.25, optic.SurfaceGroup.Items[2].MechanicalSemiDiameter);
        Assert.Equal(2, optic.SurfaceGroup.Items[2].MechanicalSemiDiameterSolveCode);
        Assert.Equal(3.5, optic.SurfaceGroup.Items[3].MechanicalSemiDiameter);
    }
}
