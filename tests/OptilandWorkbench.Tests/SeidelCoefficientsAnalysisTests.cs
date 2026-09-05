using System.Globalization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class SeidelCoefficientsAnalysisTests
{
    [Fact]
    public void GeneratesSurfaceContributionsAndCumulativeRow()
    {
        var optic = Optic.CreateCookeTriplet();
        var data = new SeidelCoefficientsAnalysis(optic).GenerateData();

        Assert.Equal("Seidel Coefficients", data.Name);
        Assert.NotNull(data.Table);
        Assert.Equal(8, data.Table!.Columns.Count);
        Assert.Equal(optic.SurfaceGroup.Items.Count, data.Table.Rows.Count);
        Assert.Equal("累计", data.Table.Rows[^1][0]);
        Assert.Contains(data.Table.Rows, row => row[0] == "光阑");
        Assert.Contains(data.Table.Rows, row => row[0] == "像面");
        Assert.All(
            data.Table.Rows,
            row => Assert.All(
                row.Skip(1),
                value => Assert.True(double.TryParse(value, CultureInfo.InvariantCulture, out _))));
        Assert.Contains("SPHA S1", data.ReportText);
        Assert.Contains("CTR (CT)", data.ReportText);
        foreach (var (title, count) in new[]
        {
            ("赛德尔像差系数", 8), ("赛德尔像差系数（波长）", 8),
            ("横向像差系数", 11), ("轴向像差系数", 11)
        })
        {
            var rows = ReportRows(data.ReportText!, title);
            Assert.Equal(data.Table.Rows.Count, rows.Length);
            Assert.Equal(data.Table.Rows.Select(row => row[0]), rows.Select(row => row[0]));
            Assert.All(rows, row => Assert.Equal(count, row.Length));
            for (var column = 1; column < count; column++)
            {
                var sum = rows.SkipLast(1).Sum(row => Number(row[column]));
                Assert.InRange(Math.Abs(sum - Number(rows[^1][column])), 0, rows.Length * 0.0000005);
            }
        }
        Assert.Contains("W220S", data.ReportText);
        Assert.Contains("W220M", data.ReportText);
        Assert.Contains("W220T", data.ReportText);
    }

    [Fact]
    public void UsesRequestedOneBasedWavelengthNumber()
    {
        var optic = Optic.CreateCookeTriplet();
        var expected = optic.Wavelengths[^1].Micrometers;

        var data = new SeidelCoefficientsAnalysis(optic, optic.Wavelengths.Count).GenerateData();

        Assert.Equal(expected, Assert.IsType<double>(data.Values["WavelengthMicrometers"]), 12);
    }

    [Fact]
    public void SurfacePresentationNamesDoNotSelectImageRow()
    {
        var optic = Optic.CreateCookeTriplet();
        var baseline = new SeidelCoefficientsAnalysis(optic).GenerateData();

        optic.SurfaceGroup.Items[1].Label = "Image";
        optic.SurfaceGroup.Items[^1].Label = "Sensor plane";
        var relabeled = new SeidelCoefficientsAnalysis(optic).GenerateData();

        Assert.Equal(
            baseline.Table!.Rows.Select(row => row[0]),
            relabeled.Table!.Rows.Select(row => row[0]));
    }

    [Fact]
    public void DiagramProvidesSevenBarSeriesAndTotalGroup()
    {
        var optic = Optic.CreateCookeTriplet();

        var data = new SeidelDiagramAnalysis(
            optic,
            maximumAberration: 0.2,
            gridInterval: 0.02).GenerateData();

        Assert.Equal("Seidel Diagram", data.Name);
        Assert.Equal(7, data.SeriesList?.Count);
        Assert.All(data.SeriesList!, series => Assert.Equal(AnalysisSeriesKind.Bar, series.Kind));
        Assert.Equal("总和", data.Table?.Rows[^1][0]);
        Assert.Equal(0.2, Assert.IsType<double>(data.Values["MaximumAberration"]), 12);
        Assert.Equal(0.02, Assert.IsType<double>(data.Values["GridInterval"]), 12);
    }

    [Fact]
    public void Captured123456ReportMatchesAllFourCoefficientTablesAtCapturedWavelengthTwo()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Fixture("zemax-123456.ZMX")), ".zmx");
        var data = new SeidelCoefficientsAnalysis(optic, wavelengthNumber: 2).GenerateData();
        var reference = File.ReadAllText(Fixture("zemax-123456-seidel-coefficients.txt"));
        foreach (var title in new[] { "赛德尔像差系数", "赛德尔像差系数（波长）", "横向像差系数", "轴向像差系数" })
        {
            var actual = ReportRows(data.ReportText!, title);
            var expected = ReportRows(reference, title);
            Assert.Equal(expected.Length, actual.Length);
            for (var row = 0; row < expected.Length; row++)
            {
                Assert.Equal(expected[row][0], actual[row][0]);
                Assert.Equal(expected[row].Length, actual[row].Length);
                for (var column = 1; column < expected[row].Length; column++)
                {
                    Assert.True(Math.Abs(Number(expected[row][column]) - Number(actual[row][column])) <= 0.000001,
                        $"{title}, surface {expected[row][0]}, column {column}: expected {expected[row][column]}, actual {actual[row][column]}");
                }
            }
        }
        Assert.Equal(-376.2850, Assert.IsType<double>(data.Values["PetzvalRadius"]), 4);
    }

    [Fact]
    public void ConversionFormulasMatchCaptured123456TablesWithinPrintedInputPrecision()
    {
        // This isolates the new conversion formulas, NOT lens-import/trace parity.
        // The baseline prints S coefficients to 6 places and u' to 4 places.
        var reference = File.ReadAllText(Fixture("zemax-123456-seidel-coefficients.txt"));
        var seidel = ReportRows(reference, "赛德尔像差系数");
        const double lambda = 0.00044;
        const double u = -0.1844;
        const double coefficientRounding = 0.0000005;
        const double slopeRounding = 0.00005;
        foreach (var title in new[] { "赛德尔像差系数（波长）", "横向像差系数", "轴向像差系数" })
        {
            var expected = ReportRows(reference, title);
            Assert.Equal(seidel.Length, expected.Length);
            for (var row = 0; row < seidel.Length; row++)
            {
                var s = seidel[row].Skip(1).Select(Number).ToArray();
                var wave = title == "赛德尔像差系数（波长）";
                var longitudinal = title == "轴向像差系数";
                var factor = longitudinal ? 1 / (2 * u * u) : -1 / (2 * u);
                var actual = wave
                    ? SeidelCoefficientsAnalysis.WaveCoefficients(s, lambda)
                    : SeidelCoefficientsAnalysis.RayAberrationCoefficients(s, factor, (longitudinal ? 2 : -2) * factor);
                var gains = wave
                    ? new[] { 1 / (8 * lambda), 1 / (2 * lambda), 1 / (2 * lambda), 1 / (4 * lambda), 1 / (2 * lambda), 1 / (2 * lambda), 1 / lambda }
                    : new[] { 1d, 1, 3, 2, 1, 2, 4, 1, 2, 2 }.Select(gain => gain * factor).ToArray();
                var slopeRelativeError = wave ? 0 : Math.Pow(Math.Abs(u) / (Math.Abs(u) - slopeRounding), longitudinal ? 2 : 1) - 1;
                for (var column = 0; column < actual.Length; column++)
                {
                    var inputError = coefficientRounding * Math.Abs(gains[column]);
                    var tolerance = inputError + ((Math.Abs(actual[column]) + inputError) * slopeRelativeError) + coefficientRounding;
                    Assert.True(Math.Abs(actual[column] - Number(expected[row][column + 1])) <= tolerance,
                        $"{title} {seidel[row][0]} column {column + 1}: {actual[column]}, expected {expected[row][column + 1]}, printed-input bound {tolerance}");
                }
            }
        }
    }

    [Fact]
    public void MsL7SelectedRowsMatchTheUserProvidedScreenshots()
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Fixture("zemax-ms-l7-high-na.ZMX")), ".zmx");
        var report = new SeidelCoefficientsAnalysis(optic, wavelengthNumber: 2).GenerateData().ReportText!;
        AssertRow("赛德尔像差系数", "1", new[] { 0.075930, 0.000533, 0.000004, 0.007243, 0.000051, -0.016594, -0.000117 });
        AssertRow("赛德尔像差系数", "累计", new[] { 0.024034, 0.004435, 0.001695, 0.004525, -0.005595, -0.001438, 0.001850 });
        AssertRow("赛德尔像差系数（波长）", "1", new[] { 16.153722, 0.453929, 0.003189, 3.081953, 0.043325, -14.121166, -0.198406 });
        AssertRow("横向像差系数", "1", new[] { 0.084092, 0.000591, 0.001772, 0.000008, 0.008022, 0.008026, 0.008034, 0.000056, 0.036755, 0.000258 });
        AssertRow("轴向像差系数", "1", new[] { 0.186261, 0.001309, 0.003926, 0.000018, 0.017768, 0.017777, 0.017796, 0.000125, -0.081412, -0.000572 });

        void AssertRow(string title, string label, double[] expected)
        {
            var row = ReportRows(report, title).Single(row => row[0] == label);
            Assert.Equal(expected.Length + 1, row.Length);
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.True(Math.Abs(expected[index] - Number(row[index + 1])) <= 0.000001,
                    $"{title} {label} column {index + 1}: {row[index + 1]}, expected {expected[index]}");
            }
        }
    }

    [Fact]
    public void SingleWavelengthHasZeroColorInEveryCoefficientRepresentation()
    {
        var optic = Optic.CreateCookeTriplet();
        while (optic.Wavelengths.Count > 1) { optic.Wavelengths.RemoveAt(optic.Wavelengths.Count - 1); }
        var report = new SeidelCoefficientsAnalysis(optic).GenerateData().ReportText!;
        foreach (var title in new[] { "赛德尔像差系数", "赛德尔像差系数（波长）", "横向像差系数", "轴向像差系数" })
        {
            Assert.All(ReportRows(report, title), row =>
            {
                Assert.Equal(0, Number(row[^2]));
                Assert.Equal(0, Number(row[^1]));
            });
        }
    }

    [Fact]
    public void PlaneInterfaceRetainsDistortionAndLateralColorWithZeroMarginalIncidence()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[0].Thickness = double.PositiveInfinity;
        optic.SurfaceGroup.Items[1].Radius = 0;
        optic.FieldDefinition = FieldDefinitionKind.Angle;
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);
        var marginal = optic.Paraxial.MarginalRay(wavelength.Micrometers);
        var chief = optic.Paraxial.ChiefRay(wavelength.Micrometers);
        Assert.Equal(0, marginal.Slopes[0][0]);
        var surface = optic.SurfaceGroup.Items[1];
        var n = surface.MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        var nBefore = optic.SurfaceGroup.Items[0].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
        var b = nBefore * chief.Slopes[0][0];
        var y = marginal.Heights[1][0];
        var expectedDistortion = -b * b * b * y * ((1 / (n * n)) - (1 / (nBefore * nBefore)));
        var shortWave = optic.Wavelengths.Min(item => item.Nanometers);
        var longWave = optic.Wavelengths.Max(item => item.Nanometers);
        var expectedColor = -b * y * (surface.MaterialAfter.RefractiveIndex(shortWave) - surface.MaterialAfter.RefractiveIndex(longWave)) / n;
        var row = new SeidelCoefficientsAnalysis(optic).GenerateData().Table!.Rows[0];
        Assert.True(Math.Abs(expectedDistortion) > 0.000001);
        Assert.True(Math.Abs(expectedColor) > 0.000001);
        Assert.Equal(expectedDistortion, Number(row[5]), 6);
        Assert.Equal(expectedColor, Number(row[7]), 6);
    }

    [Fact]
    public void CollimatedImageSpaceMarksUndefinedConversionsInsteadOfZeroOrInfinity()
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var surface in optic.SurfaceGroup.Items) { surface.Radius = 0; }
        var data = new SeidelCoefficientsAnalysis(optic).GenerateData();
        Assert.Equal(0, Assert.IsType<double>(data.Values["MarginalRaySlopeImageSpace"]));
        foreach (var title in new[] { "横向像差系数", "轴向像差系数" })
        {
            Assert.All(ReportRows(data.ReportText!, title), row => Assert.All(row.Skip(1), cell => Assert.Equal("—", cell)));
        }
        Assert.Contains("换算未定义", data.ReportText);
    }

    private static string[][] ReportRows(string report, string title)
    {
        var lines = report.Replace("\r", "").Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim().TrimEnd(':', '：') == title);
        Assert.True(start >= 0, $"Missing report section: {title}");
        return lines.Skip(start + 1).SkipWhile(string.IsNullOrWhiteSpace).Skip(1)
            .TakeWhile(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToArray();
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
}
