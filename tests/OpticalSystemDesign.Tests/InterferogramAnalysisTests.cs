using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class InterferogramAnalysisTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(0.25, 0.5)]
    [InlineData(0.5, 0)]
    [InlineData(-0.5, 0)]
    [InlineData(1, 1)]
    [InlineData(1000000000000.25, 0.5)]
    public void NormalizedIntensityHasUnityZeroPhaseAndHalfWaveZero(double phase, double expected) =>
        Assert.Equal(expected, InterferogramAnalysis.NormalizedIntensity(phase), 12);

    [Fact]
    public void ReferenceBeamsHaveAnalyticalTiltFringesIndependentOfScale()
    {
        var optic = Lens();
        var s = Settings() with { Beam1 = 0, Beam2 = 0, XTilt = 4, YTilt = -2 };
        var a = new InterferogramAnalysis(optic, s).GenerateData();
        var b = new InterferogramAnalysis(optic, s with { ScaleFactor = 2 }).GenerateData();
        Assert.Equal(a.PlotSeries[0].Points, b.PlotSeries[0].Points);
        foreach (var point in a.PlotSeries[0].Points)
            Assert.Equal(Math.Pow(Math.Cos(Math.PI * (2 * point.X - point.Y)), 2), point.Value!.Value, 11);
        Assert.Equal(AnalysisAxisQuantity.Intensity, a.PlotSeries[0].ValueQuantity);
        Assert.Equal(AnalysisAxisUnit.Dimensionless, a.PlotSeries[0].ValueUnit);
        Assert.Equal(0, a.PlotSeries[0].ValueMinimum); Assert.Equal(1, a.PlotSeries[0].ValueMaximum);
    }

    [Fact]
    public void IdenticalConfigurationBeamsCancelExactly()
    {
        var optic = Lens();
        var data = new InterferogramAnalysis(optic, Settings() with { Beam1 = 1, Beam2 = 1, ConsiderPathLengthDifference = true }, [optic]).GenerateData();
        Assert.All(data.PlotSeries[0].Points, p => Assert.Equal(1, p.Value));
        Assert.Equal(0d, data.Values["PeakToValleyWaves"]);
    }

    [Fact]
    public void SharedCoreOpdRemainsAvailableSeparatelyFromFringeIntensity()
    {
        var optic = Lens(); var data = new InterferogramAnalysis(optic, Settings()).GenerateData();
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var points = data.PlotSeries[0].Points;
        var traced = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave,
            points.Select(p => (p.X, p.Y)).ToArray(), aimAtStop: optic.RayAimingEnabled);
        Assert.Equal(points.Count, data.Table!.Rows.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var opd = double.Parse(data.Table.Rows[i][2], CultureInfo.InvariantCulture);
            Assert.Equal(traced.Samples[i].OpdWaves, opd, 12);
            Assert.Equal(Math.Pow(Math.Cos(Math.PI * opd), 2), points[i].Value!.Value, 10);
        }
        Assert.DoesNotContain("OPD Map", data.PlotOptions!.Title);
    }

    [Fact]
    public void DoublePassScalesOpdBeforeAddingTilt()
    {
        var optic = Lens(); var s = Settings() with { XTilt = 3, YTilt = 2 };
        var a = new InterferogramAnalysis(optic, s).GenerateData();
        var b = new InterferogramAnalysis(optic, s with { ScaleFactor = 2 }).GenerateData();
        for (var i = 0; i < b.Table!.Rows.Count; i++)
        {
            var row = b.Table.Rows[i]; var x = Number(row[0]); var y = Number(row[1]); var opd = Number(row[2]);
            Assert.Equal(2 * opd + (3 * x + 2 * y) / 2, Number(row[3]), 11);
            Assert.Equal(Number(a.Table!.Rows[i][2]), opd, 12);
        }
    }

    [Fact]
    public void HalfWaveChiefPathDifferenceComplementsTheFringes()
    {
        var optic = Lens(); var second = Optic.FromSnapshot(optic.ToSnapshot());
        var lambdaMm = optic.Wavelengths.First(w => w.IsPrimary).Micrometers * 1e-3;
        second.SurfaceGroup.Items[^2].Thickness += lambdaMm / 2; second.SurfaceGroup.Renumber();
        var s = Settings() with { Beam1 = 1, Beam2 = 2 };
        var without = new InterferogramAnalysis(optic, s, [optic, second]).GenerateData();
        var with = new InterferogramAnalysis(optic, s with { ConsiderPathLengthDifference = true }, [optic, second]).GenerateData();
        Assert.Equal(-lambdaMm / 2, (double)with.Values["PathLengthDifferenceMillimeters"], 9);
        for (var i = 0; i < with.PlotSeries[0].Points.Count; i++)
            Assert.Equal(1 - without.PlotSeries[0].Points[i].Value!.Value, with.PlotSeries[0].Points[i].Value!.Value, 7);
    }

    [Fact]
    public void SwappingBeamsPreservesIntensityAndReversesSignedPhase()
    {
        var optic = Lens(); var second = Optic.FromSnapshot(optic.ToSnapshot());
        second.SurfaceGroup.Items[2].Radius *= 1.001;
        var s = Settings() with { Beam1 = 1, Beam2 = 2, ConsiderPathLengthDifference = true };
        var a = new InterferogramAnalysis(optic, s, [optic, second]).GenerateData();
        var b = new InterferogramAnalysis(optic, s with { Beam1 = 2, Beam2 = 1 }, [optic, second]).GenerateData();
        for (var i = 0; i < a.Table!.Rows.Count; i++)
        {
            Assert.Equal(-Number(a.Table.Rows[i][3]), Number(b.Table!.Rows[i][3]), 11);
            Assert.Equal(a.PlotSeries[0].Points[i].Value, b.PlotSeries[0].Points[i].Value);
        }
    }

    [Fact]
    public void DisplayModesAndExitPupilShapeDoNotChangeNumericalIntensity()
    {
        var optic = Lens(); var s = Settings() with { FieldNumber = 2 };
        var baseline = new InterferogramAnalysis(optic, s).GenerateData();
        foreach (var mode in new[] { "灰度", "伪彩色", "等高线" })
        {
            var data = new InterferogramAnalysis(optic, s with { ShowAs = mode, UseExitPupilShape = true }).GenerateData();
            Assert.Equal(baseline.PlotSeries[0].Points, data.PlotSeries[0].Points);
            Assert.Equal(mode == "伪彩色" ? AnalysisColorMap.Jet : AnalysisColorMap.InverseGray, data.PlotSeries[0].ColorMap);
        }
    }

    [Fact]
    public void SubapertureSamplesActualShiftedPupilAndPreservesPrescription()
    {
        var optic = Lens(); var frozen = JsonSerializer.Serialize(optic.ToSnapshot());
        var s = Settings() with { Sx = 0.6, Sy = 0.1, Sr = 0.5 };
        var data = new InterferogramAnalysis(optic, s).GenerateData();
        Assert.All(data.PlotSeries[0].Points, p =>
        {
            Assert.True(p.X * p.X + p.Y * p.Y <= 1 + 1e-12);
            Assert.True(Math.Pow(p.X - s.Sx, 2) + Math.Pow(p.Y - s.Sy, 2) <= s.Sr * s.Sr + 1e-12);
        });
        Assert.Equal(frozen, JsonSerializer.Serialize(optic.ToSnapshot()));
        Assert.Equal(s.Sx - s.Sr, data.PlotOptions!.XMinimum);
    }

    [Fact]
    public void IntermediateSurfaceUsesAnalysisCopyAndParaxialFocus()
    {
        var optic = Lens(); var frozen = JsonSerializer.Serialize(optic.ToSnapshot());
        var target = optic.SurfaceGroup.Items[^2].Number;
        var data = new InterferogramAnalysis(optic, Settings() with { SurfaceNumber = target }).GenerateData();
        Assert.Equal(target, data.Values["SurfaceNumber"]);
        Assert.NotEmpty(data.PlotSeries[0].Points);
        Assert.All(data.PlotSeries[0].Points, p => Assert.InRange(p.Value!.Value, 0, 1));
        Assert.Equal(frozen, JsonSerializer.Serialize(optic.ToSnapshot()));
    }

    [Fact]
    public void VertexReferenceRetainsOffAxisTiltAndRequiresUnitScale()
    {
        var optic = Lens(); var s = Settings() with { FieldNumber = 2 };
        var a = new InterferogramAnalysis(optic, s).GenerateData();
        var b = new InterferogramAnalysis(optic, s with { RefBeamToVertex = true }).GenerateData();
        Assert.Contains(a.PlotSeries[0].Points.Zip(b.PlotSeries[0].Points), pair => Math.Abs(pair.First.Value!.Value - pair.Second.Value!.Value) > 0.01);
        Assert.Throws<ArgumentException>(() => new InterferogramAnalysis(optic, s with { ScaleFactor = 2, RefBeamToVertex = true }));
    }

    [Fact]
    public void ContourFormatSupportsIntervalAndExplicitLevelsWithBoundedFallback()
    {
        Assert.Equal(new[] { .2, .4, .6, .8 }, InterferogramAnalysis.ContourLevels("0.2"), new DoubleComparer());
        Assert.Equal(new[] { .2, .5, .8 }, InterferogramAnalysis.ContourLevels("0.8 0.5 0.2"));
        Assert.Equal(InterferogramAnalysis.ContourLevels(""), InterferogramAnalysis.ContourLevels("0.00001"));
        Assert.Throws<ArgumentException>(() => InterferogramAnalysis.ContourLevels("0"));
        Assert.Throws<ArgumentException>(() => InterferogramAnalysis.ContourLevels("NaN"));
    }

    [Fact]
    public void SamplingSelectionTypedMetadataAndUnavailableInputsAreHonored()
    {
        var optic = Lens(); var runtime = new WorkbenchRuntime(optic);
        var data = runtime.BuildAnalysisData("Interferogram", new Dictionary<string, string>
        { ["Sampling"] = "32 x 32", ["FieldNumber"] = "2", ["WavelengthNumber"] = "1", ["XTilt"] = "3" });
        Assert.Equal("32 x 32", data.Values["Sampling"]);
        Assert.Equal(optic.Wavelengths[0].Micrometers, data.Values["WavelengthMicrometers"]);
        Assert.Equal(3d, data.Values["XTilt"]);
        Assert.Contains(runtime.GetAnalysisParameters("Interferogram"), p => p.Key == "ContourFormat" && p.Kind == OpticalSystemDesign.Application.Runtime.AnalysisParameterKind.Text);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new InterferogramAnalysis(optic, Settings()).GenerateData(cancellation.Token));
        optic.Wavelengths.Clear();
        Assert.Equal(AnalysisOutcome.Unavailable, new InterferogramAnalysis(optic, Settings()).GenerateData().Outcome);
    }

    [Fact]
    public void CurrentBeamLabelsPreserveNegativeConfigurationIdentifiers()
    {
        var data = new WorkbenchRuntime(Lens()).BuildAnalysisData("Interferogram", new Dictionary<string, string>
        {
            ["Sampling"] = "32 x 32",
            ["Beam1"] = "-1 - 当前结构",
            ["Beam2"] = "-1 - 当前结构",
            ["SurfaceNumber"] = "-1 - 像面",
            ["ConsiderPathLengthDifference"] = "true"
        });
        Assert.Equal(-1, data.Values["Beam1"]);
        Assert.Equal(-1, data.Values["Beam2"]);
        Assert.All(data.PlotSeries[0].Points, point => Assert.Equal(1, point.Value));
        Assert.Equal(0d, data.Values["PeakToValleyWaves"]);
    }

    [Fact]
    public void IntermediateSurfaceBeforeAnAimedStopIsExplicitlyUnsupported()
    {
        var optic = Lens();
        foreach (var surface in optic.SurfaceGroup.Items) surface.IsStop = false;
        optic.SurfaceGroup.Items[^2].IsStop = true;
        optic.RayAimingEnabled = true;
        var frozen = JsonSerializer.Serialize(optic.ToSnapshot());
        Assert.Throws<NotSupportedException>(() => new InterferogramAnalysis(optic,
            Settings() with { SurfaceNumber = optic.SurfaceGroup.Items[1].Number }).GenerateData());
        Assert.Equal(frozen, JsonSerializer.Serialize(optic.ToSnapshot()));
    }

    [Fact]
    public void CapturedZemax2026R1InterferogramMatchesItsPrintedGrid()
    {
        var optic = Lens();
        var data = new InterferogramAnalysis(optic, new(Sampling: 64, WavelengthNumber: 2, FieldNumber: 1, UseExitPupilShape: false)).GenerateData();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456-interferogram.json")));
        var grid = json.RootElement.GetProperty("dataGrids")[0].GetProperty("values");
        var printed = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456-interferogram.txt"))
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(row => row.Length == 64).Select(row => row.Select(Number).ToArray()).ToArray();
        Assert.Equal(64, printed.Length);
        var errors = new List<double>();
        var detailed = new List<object>();
        foreach (var point in data.PlotSeries[0].Points)
        {
            var column = (int)Math.Round(point.X * 31 + 32);
            var row = (int)Math.Round(point.Y * 31 + 32);
            // Match the captured image-array orientation to the shared pupil
            // indices. The independent printed listing reverses only the API
            // array's row order; it also verifies the five-decimal values.
            var expected = grid[63 - row][63 - column].GetDouble();
            Assert.InRange(Math.Abs(expected - printed[row][63 - column]), 0, .0000050001);
            errors.Add(Math.Abs(expected - point.Value!.Value));
            detailed.Add(new { point.X, point.Y, Column = column, Row = row, Actual = point.Value, Expected = expected });
        }
        var auditDirectory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_INTERFEROGRAM_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(auditDirectory))
        {
            Directory.CreateDirectory(auditDirectory);
            File.WriteAllText(Path.Combine(auditDirectory, "native-grid-comparison.json"), JsonSerializer.Serialize(new
            { Points = detailed, MaximumError = errors.Max(), PeakToValley = data.Values["PeakToValleyWaves"] }));
        }
        Assert.True(errors.Max() <= 1e-8, $"Captured raw intensity: maximum error {errors.Max():G17}");
        Assert.InRange((double)data.Values["PeakToValleyWaves"], .12615, .12625);
    }

    private sealed class DoubleComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-12;
        public int GetHashCode(double obj) => 0;
    }
    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    internal static InterferogramSettings Settings() => new(Sampling: 16, UseExitPupilShape: false);
    internal static Optic Lens() => OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");
}
