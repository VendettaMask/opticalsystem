using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class NativeRaySurfaceMapTests
{
    [Fact]
    public void CoincidentGradient9MirrorRecoversTheLocalDirectionOnlyWithMatchingEvidence()
    {
        var root = Directory.CreateTempSubdirectory("native-gradient9-mirror-");
        try
        {
            var native = Path.Combine(root.FullName, "data.json");
            var proof = Path.Combine(root.FullName, "workbench.json");
            JsonFiles.Write(native, new { });
            File.WriteAllText(Path.Combine(root.FullName, "model.json"), """
                {"surfaces":[{"Type":"Standard","Material":""},{"Type":"Gradient9","Material":"SLS"},
                {"Type":"Standard","Material":"MIRROR","Thickness":0},{"Type":"Gradient9","Material":"SLS"},
                {"Type":"Standard","Material":""}]}
                """);
            const double ratio = .99;
            const double physicalAngle = .2;
            var axialSine = ratio * Math.Sin(physicalAngle);
            var axialAngle = Math.Asin(axialSine);
            string Row(int n, double m, double z, double angle, double length) =>
                FormattableString.Invariant($"{n} 0 .25 0 0 {m:R} {z:R} 0 0 -1 {angle:R} {length:R}");
            File.WriteAllLines(Path.ChangeExtension(native, ".txt"), [
                Row(1, 0, 1, 0, 0),
                Row(2, axialSine, Math.Cos(axialAngle), physicalAngle * 180 / Math.PI, 0),
                Row(3, Math.Sin(physicalAngle), Math.Cos(physicalAngle), axialAngle * 180 / Math.PI, -1e-13),
                Row(4, 0, 1, 0, 0)
            ]);
            var entry = AnalysisComparisonRegistry.Get("Single Ray Trace");
            var request = new CanonicalAnalysisRequest
            {
                CanonicalAnalysisKey = entry.CanonicalAnalysisKey, Apodization = "Uniform",
                WorkbenchSettings = [], SurfaceCount = 5
            };
            NumericResult Read(double indexRatio)
            {
                JsonFiles.Write(proof, new AnalysisData("Single Ray Trace",
                    new Dictionary<string, object> { ["Gradient9MirrorIndexRatios"] = new[] { new[] { 2d, indexRatio } } }));
                return ResultNormalizer.Zemax(native, entry, request, proof);
            }

            var matched = Read(ratio);
            Assert.Equal(-Math.Sin(physicalAngle), matched.Series.Single(series => series.Id == "m").Y[1], 12);
            Assert.Equal(physicalAngle * 180 / Math.PI, matched.Series.Single(series => series.Id == "incidence").Y[2], 12);
            var mismatched = Read(.95);
            Assert.Equal(-axialSine, mismatched.Series.Single(series => series.Id == "m").Y[1]);
            Assert.Equal(axialAngle * 180 / Math.PI, mismatched.Series.Single(series => series.Id == "incidence").Y[2]);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void NonzeroThicknessBreakIsRetainedWhileZeroThicknessBreakStillFolds()
    {
        using var model = JsonDocument.Parse("""{"surfaces":[{"Type":"Standard"},{"Type":"Standard"},{"Type":"CoordinateBreak","Thickness":0},{"Type":"CoordinateBreak","Thickness":-5},{"Type":"Standard"}]}""");
        double[][] rows = Enumerable.Range(1, 4).Select(i => new double[] { i, i, 0, 0, 0, 0, 1, 0, 0, -1, 0, i }).ToArray();
        var current = NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 4);
        Assert.Equal(new[] { 1d, 3, 4 }, current.Select(row => row[1]));
        Assert.Equal(new[] { 1d, 5, 4 }, current.Select(row => row[11]));
        var historical = NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 3);
        Assert.Equal(new[] { 1d, 4 }, historical.Select(row => row[1]));
        Assert.Equal(new[] { 1d, 9 }, historical.Select(row => row[11]));
    }

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
    public void AnExplicitlyIgnoredMirrorOmitsOnlyThatRowAndDoesNotReverseDirections()
    {
        using var model = JsonDocument.Parse("""{"surfaces":[{"Type":"Standard","Material":""},{"Type":"Standard","Material":""},{"Type":"Standard","Material":"MIRROR"},{"Type":"Standard","Material":""}]}""");
        double[][] rows = [[1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 2], [3, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 3]];
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 4));
        Assert.Same(rows, NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 4, [2]));
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 4, [1]));
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows[..1], model.RootElement, 4, [2]));
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 3, [2]));
        Assert.Equal(new[] { 1, 1, 1, 1 }, NativeReflectionConvention.PropagationSigns(model.RootElement, [2]));
    }

    [Fact]
    public void SparseReportRequiresMatchingImportedOmissionsAndAllActiveSurfaces()
    {
        var root = Directory.CreateTempSubdirectory("ignored-ray-report-");
        try
        {
            var native = Path.Combine(root.FullName, "data.json");
            var workbench = Path.Combine(root.FullName, "workbench.json");
            JsonFiles.Write(native, new { });
            File.WriteAllText(Path.Combine(root.FullName, "model.json"),
                """{"surfaces":[{"Type":"Standard","Material":""},{"Type":"Standard","Material":""},{"Type":"Standard","Material":""},{"Type":"Standard","Material":""}]}""");
            JsonFiles.Write(workbench, new AnalysisData("Single Ray Trace", new Dictionary<string, object>
            {
                ["IgnoredSurfaceNumbers"] = new[] { 1 },
                ["RealRayData"] = new double[][] { [2, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0], [3, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0] }
            }));
            var entry = AnalysisComparisonRegistry.Get("Single Ray Trace");
            var request = new CanonicalAnalysisRequest
            {
                CanonicalAnalysisKey = entry.CanonicalAnalysisKey, Apodization = "Uniform", WorkbenchSettings = [], SurfaceCount = 4
            };
            const string cells = " 0 0 0 0 0 1 0 0 -1 0 0";
            var report = Path.ChangeExtension(native, ".txt");
            File.WriteAllLines(report, ["2" + cells, "3" + cells]);
            Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(native, entry, request));
            var result = ResultNormalizer.Zemax(native, entry, request, workbench);
            Assert.All(result.Series, series => Assert.Equal(new[] { 2d, 3 }, series.X));

            File.WriteAllLines(report, ["1" + cells, "2" + cells, "3" + cells]);
            Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(native, entry, request, workbench));
            File.WriteAllLines(report, ["2" + cells]);
            Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(native, entry, request, workbench));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("GridGradient")]
    [InlineData("Gradient1")]
    [InlineData("Gradient9")]
    public void UnreportedGradientLengthRetainsOtherColumnsAndCannotPassTheFullReport(string surfaceType)
    {
        var root = Directory.CreateTempSubdirectory("grid-gradient-ray-report-");
        try
        {
            var native = Path.Combine(root.FullName, "data.json");
            var workbench = Path.Combine(root.FullName, "workbench.json");
            JsonFiles.Write(native, new { });
            File.WriteAllText(Path.Combine(root.FullName, "model.json"),
                $$"""{"surfaces":[{"Type":"Standard","Material":""},{"Type":"{{surfaceType}}","Material":"","Thickness":3.8},{"Type":"Standard","Material":""},{"Type":"Standard","Material":""}]}""");
            const string prefix = " 0 0 0 0 0 1 0 0 -1 0 ";
            File.WriteAllLines(Path.ChangeExtension(native, ".txt"), ["1" + prefix + "1", "2" + prefix + "0", "3" + prefix + "0"]);
            JsonFiles.Write(workbench, new AnalysisData("Single Ray Trace", new Dictionary<string, object>
            {
                ["GradientSegmentEndSurfaces"] = new[] { 2 },
                ["RealRayData"] = new double[][]
                {
                    [1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1],
                    [2, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 3.8],
                    [3, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0]
                }
            }));
            var entry = AnalysisComparisonRegistry.Get("Single Ray Trace");
            var request = new CanonicalAnalysisRequest
            {
                CanonicalAnalysisKey = entry.CanonicalAnalysisKey, Apodization = "Uniform", WorkbenchSettings = [], SurfaceCount = 4
            };
            Assert.Null(ResultNormalizer.Zemax(native, entry, request).UnreportedNativeRayPathSurfaces);
            var expected = ResultNormalizer.Zemax(native, entry, request, workbench);
            var actual = ResultNormalizer.Workbench(workbench, entry, request);
            Assert.Equal(new[] { 2 }, expected.UnreportedNativeRayPathSurfaces);
            Assert.Equal(new[] { 1d, 3 }, expected.Series.Single(series => series.Id == "path-length").X);
            Assert.All(expected.Series.Where(series => series.Id != "path-length"), series => Assert.Equal(3, series.X.Length));

            var run = new AnalysisRun { Key = entry.CanonicalAnalysisKey, Directory = "ray", Request = request };
            ComparisonRunner.Compare(run, entry, new AnalysisConfiguration { Quantities = new(entry.DefaultTolerances) },
                actual, expected, root.FullName);
            Assert.Equal(Conclusion.Incomparable, run.Conclusion);
            Assert.Contains("unreported at surface(s) 2", run.Reason);
            Assert.All(run.Metrics, metric => Assert.Equal(Conclusion.Pass, metric.Conclusion));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void PartlyRetainedCoordinatePlanesCannotBeGuessedFromCounts()
    {
        using var model = JsonDocument.Parse("""{"surfaces":[{"Type":"Standard"},{"Type":"CoordinateBreak"},{"Type":"CoordinateBreak"},{"Type":"Standard"}]}""");
        double[][] rows = Enumerable.Range(1, 3).Select(i => new double[] { i, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, i }).ToArray();
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 3));
        var mapped = NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 3, sourceSurfaceNumbers: [0, 2, 3]);
        Assert.Equal(new[] { 1d, 2 }, mapped.Select(row => row[0]));
        Assert.Equal(new[] { 3d, 3 }, mapped.Select(row => row[11]));
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 3,
            sourceSurfaceNumbers: [0, 3, 3]));
        (rows[0], rows[1]) = (rows[1], rows[0]);
        Assert.Throws<InvalidDataException>(() => NativeRaySurfaceMap.ToImportedSurfaces(rows, model.RootElement, 2));
    }
}
