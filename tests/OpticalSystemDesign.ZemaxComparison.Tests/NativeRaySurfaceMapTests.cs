using System.Text.Json;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class NativeRaySurfaceMapTests
{
    [Fact]
    public void FoldedPlanesPreservePhysicalHitAndAccumulateSegmentLengthsWithoutChangingRawReport()
    {
        using var model = JsonDocument.Parse("""{"surfaces":[{"Type":"Standard"},{"Type":"Standard"},{"Type":"CoordinateBreak"},{"Type":"BirefringentOut"},{"Type":"CoordinateBreak"},{"Type":"Standard"}]}""");
        double[][] rows = Enumerable.Range(1, 5).Select(i => new double[] { i, i * 2, i * 3, 0, 0, .1 * i, 1, 0, 0, -1, i * 10, i + .25 }).ToArray();
        var mapped = NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 4);
        Assert.Equal(new[] { 1d, 2, 3 }, mapped.Select(row => row[0]));
        Assert.Equal(rows[2][1..11], mapped[1][1..11]);
        Assert.Equal(new[] { 1.25, 5.5, 9.5 }, mapped.Select(row => row[11]));
        Assert.Equal(3, rows[2][0]);
        Assert.Equal(3.25, rows[2][11]);
    }

    [Fact]
    public void AlreadyEquivalentSurfaceCountsRetainReferencePlanes()
    {
        using var model = JsonDocument.Parse("""{"surfaces":[{"Type":"Standard"},{"Type":"CoordinateBreak"},{"Type":"Standard"}]}""");
        double[][] rows = [[1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 2], [2, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 3]];
        Assert.Same(rows, NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 3));
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows[..1], model.RootElement, 2));
    }

    [Fact]
    public void PartlyRetainedCoordinatePlanesCannotBeGuessedFromCounts()
    {
        using var model = JsonDocument.Parse("""{"surfaces":[{"Type":"Standard"},{"Type":"CoordinateBreak"},{"Type":"CoordinateBreak"},{"Type":"Standard"}]}""");
        double[][] rows = Enumerable.Range(1, 3).Select(i => new double[] { i, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, i }).ToArray();
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 3));
        (rows[0], rows[1]) = (rows[1], rows[0]);
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 2));
    }
}
