using System.Globalization;
using System.Text.RegularExpressions;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using Xunit.Abstractions;

namespace OptilandWorkbench.Tests;

public sealed partial class ZemaxYYbarParityTests
{
    private readonly ITestOutputHelper _output;

    public ZemaxYYbarParityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ImageHeightNormalizedChiefRayMatchesZemax123456()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(
            File.ReadAllText(Path.Combine(fixtureDirectory, "zemax-123456.ZMX")),
            ".zmx");
        var reference = ParseReference(Path.Combine(fixtureDirectory, "zemax-123456-y-ybar.txt"));

        var data = new YYbarAnalysis(optic, zemaxCompatible: true).GenerateData();
        Assert.Equal(1, data.Values["FirstSurface"]);
        Assert.Equal(22, data.Values["LastSurface"]);
        Assert.Equal(22, data.Values["SurfaceCount"]);

        var errors = new List<double>();
        foreach (var row in reference)
        {
            var ybar = Assert.IsType<double>(data.Values[$"Surface {row.Surface} Chief"]);
            var y = Assert.IsType<double>(data.Values[$"Surface {row.Surface} Marginal"]);
            errors.Add(ybar - row.Ybar);
            errors.Add(y - row.Y);
        }

        var rmse = Math.Sqrt(errors.Average(error => error * error));
        var maximum = errors.Max(Math.Abs);
        _output.WriteLine($"RMSE={rmse:G8}; max={maximum:G8}");
        Assert.True(rmse <= 1e-8 && maximum <= 5e-8,
            $"RMSE={rmse:G8}; max={maximum:G8}");

        Assert.Equal(22, data.PlotSeries.Count);
        Assert.DoesNotContain(data.PlotSeries.SelectMany(series => series.Points),
            point => Math.Abs(point.X) > 5 || Math.Abs(point.Y) > 5);
        Assert.Equal("Y", data.PlotOptions?.Title);
        Assert.True(data.PlotOptions?.EqualAspect);
        Assert.False(data.PlotOptions?.ShowLegend);
        Assert.True(data.PlotOptions?.DefaultSquareViewport);
    }

    private static IReadOnlyList<(int Surface, double Ybar, double Y)> ParseReference(string path)
    {
        return File.ReadLines(path)
            .Select(line => DataRowRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => (
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
            .ToArray();
    }

    [GeneratedRegex(@"^\s*(\d+)\s+([-+\d.E]+)\s+([-+\d.E]+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex DataRowRegex();
}
