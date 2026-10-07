using OptilandWorkbench.ZemaxComparison;
using OptilandWorkbench.ZemaxComparison.Normalization;

namespace OptilandWorkbench.ZemaxComparison.Tests;

public sealed class StandardSampleReportTests
{
    private const string RealRows = """
          1 0 4 .3 0 -.07 .99 0 .18 -.98 10.4 .3 First surface
          2 0 3.8 -.01 0 -.12 .99 0 -.008 -.99 4.5 2.8 Last surface
        """;

    [Theory]
    [InlineData("OBJ Infinity Infinity Infinity 0 0 1 - - - - -")]
    [InlineData("物面 无限 无限 无限 0 0 1 - - - - -")]
    [InlineData("Object 0 0 0 0 0 1 - - - - -")]
    public void RealRayNumbersDoNotDependOnObjectMarkerLanguage(string objectRow)
    {
        var result = Normalize("Real ray report\n" + objectRow + "\n" + RealRows);
        Assert.Equal(11, result.Series.Count);
        Assert.Equal(new[] { 1d, 2d }, result.Series[0].X);
        Assert.Equal(new[] { 4d, 3.8 }, result.Series.Single(s => s.Id == "y").Y);
        Assert.Equal("Millimeter", result.Series.Single(s => s.Id == "y").YAxis.Unit);
        Assert.Equal(new[] { -.07, -.12 }, result.Series.Single(s => s.Id == "m").Y);
        Assert.Equal(new[] { .3, 2.8 }, result.Series.Single(s => s.Id == "path-length").Y);
    }

    [Fact]
    public void ParaxialRowsCannotReplaceTheRealRayTable()
    {
        var report = RealRows + "\n\nParaxial ray report\nOBJ Infinity Infinity Infinity 0 0 1\n"
            + "1 0 99 0 0 .5 .5\n2 0 88 0 0 .4 .4\n";
        var result = Normalize(report);
        Assert.Equal(new[] { 4d, 3.8 }, result.Series.Single(s => s.Id == "y").Y);
        Assert.Throws<InvalidDataException>(() => Normalize("1 0 99 0 0 .5 .5\n2 0 88 0 0 .4 .4\n"));
    }

    [Theory]
    [InlineData("1 0 4 .3 0 -.07 .99 0 .18 -.98 10.4 .3")]
    [InlineData("2 0 3.8 -.01 0 -.12 .99 0 -.008 -.99 4.5 2.8\n1 0 4 .3 0 -.07 .99 0 .18 -.98 10.4 .3")]
    [InlineData("1 0 4 .3 0 -.07 .99 0 .18 -.98 10.4 .3\n2 0 NaN -.01 0 -.12 .99 0 -.008 -.99 4.5 2.8")]
    public void IncompleteUnorderedAndNonfiniteTablesAreRejected(string report)
        => Assert.Throws<InvalidDataException>(() => Normalize(report));

    [Fact]
    public void MultipleRealRayTablesAreAmbiguous()
        => Assert.Throws<InvalidDataException>(() => Normalize(RealRows + "\n\n" + RealRows));

    private static NumericResult Normalize(string report)
    {
        var directory = Path.Combine(Path.GetTempPath(), "zemax-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "data.json");
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), report);
            return ExtendedResultNormalizer.Zemax(path, new CanonicalAnalysisRequest
            {
                CanonicalAnalysisKey = "Single Ray Trace",
                SurfaceCount = 3,
                Apodization = "None",
                WorkbenchSettings = []
            });
        }
        finally
        {
            File.Delete(Path.Combine(directory, "data.txt"));
            Directory.Delete(directory);
        }
    }
}
