using System.Text.Json;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class NonSequentialTeachingSampleTests
{
    [Fact]
    public void TeachingSampleManifestMatchesTrackedProjects()
    {
        var directory = SampleDirectory();
        var manifest = ReadManifest(directory);
        Assert.Equal(2, manifest.Version);
        Assert.Equal("Millimeter", manifest.LengthUnit);
        Assert.Equal(12, manifest.Samples.Count);
        Assert.Equal(
            manifest.Samples.Select(item => item.File).Order(StringComparer.Ordinal).ToArray(),
            Directory.EnumerateFiles(directory, "*.staropt")
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Equal(manifest.Samples.Count, manifest.Samples.Select(item => item.File).Distinct().Count());
        Assert.All(manifest.Samples, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Lesson));
            Assert.NotEmpty(item.SuggestedFilters);
            Assert.NotEmpty(item.DetectorResults);
            if (item.SourceKind is not null)
            {
                Assert.False(string.IsNullOrWhiteSpace(item.PreviewFile));
                var previewPath = Path.Combine(directory, item.PreviewFile!.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(previewPath), $"Missing preview: {item.PreviewFile}");
                Assert.Contains("<svg", File.ReadAllText(previewPath), StringComparison.Ordinal);
            }
        });
    }

    [Theory]
    [InlineData("01-basic-lens-detector.staropt")]
    [InlineData("02-fresnel-main-and-ghost.staropt")]
    [InlineData("03-total-internal-reflection-light-pipe.staropt")]
    [InlineData("04-two-mirror-folded-path.staropt")]
    [InlineData("05-three-wavelength-sources.staropt")]
    [InlineData("06-embedded-stl-baffle.staropt")]
    [InlineData("07-ellipse-source-footprint.staropt")]
    [InlineData("08-two-angle-anisotropic-source.staropt")]
    [InlineData("09-radial-intensity-distribution.staropt")]
    [InlineData("10-volume-rectangle-source.staropt")]
    [InlineData("11-volume-ellipse-source.staropt")]
    [InlineData("12-volume-cylinder-source.staropt")]
    public async Task TeachingSampleLoadsAndReproducesDocumentedTrace(string fileName)
    {
        var directory = SampleDirectory();
        var expected = Assert.Single(ReadManifest(directory).Samples, item => item.File == fileName);
        var project = await StarOptProjectStore.LoadAsync(Path.Combine(directory, fileName));
        var document = Assert.IsType<NonSequentialDocument>(project.NonSequentialDocument);
        var optic = project.Configurations[project.ActiveConfigurationIndex];

        document.Validate();
        Assert.Equal(expected.ObjectCount, document.Objects.Count);
        Assert.Equal(expected.MeshAssetCount, document.MeshAssets.Count);
        var trace = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(OutputMode: NonSequentialTraceOutputMode.InMemory));

        Assert.Equal(expected.BranchCount, trace.TotalBranchCount);
        Assert.Equal(expected.SourcePowerWatts, trace.EnergyBalance.SourcePowerWatts, 10);
        Assert.Equal(expected.DetectorPowerWatts, trace.EnergyBalance.DetectorPowerWatts, 10);
        Assert.Equal(expected.AbsorbedPowerWatts, trace.EnergyBalance.AbsorbedPowerWatts, 10);
        Assert.Equal(trace.EnergyBalance.SourcePowerWatts, trace.EnergyBalance.AccountedPowerWatts, 8);
        Assert.Equal(expected.DetectorResults.Count, trace.Detectors.Count);
        Assert.Equal(
            expected.DetectorResults.Select(item => item.PowerWatts).ToArray(),
            trace.Detectors.Select(item => item.TotalPowerWatts).ToArray(),
            new DoubleArrayComparer(1e-10));
        foreach (var expression in expected.SuggestedFilters)
        {
            var filter = NonSequentialPathFilter.Parse(expression);
            Assert.Contains(trace.Branches, branch => filter.IsMatch(document, branch));
        }

        switch (fileName)
        {
            case "01-basic-lens-detector.staropt":
                Assert.Contains(document.Objects, item => item.Kind == NonSequentialObjectKind.StandardLens);
                break;
            case "02-fresnel-main-and-ghost.staropt":
                Assert.Equal(2, trace.Detectors.Count(item => item.TotalPowerWatts > 0));
                Assert.True(trace.TotalBranchCount > trace.EnergyBalance.SourcePowerWatts * 800);
                break;
            case "03-total-internal-reflection-light-pipe.staropt":
                Assert.Contains(trace.Branches.SelectMany(item => item.Segments),
                    item => item.InteractionKind == RayInteractionKind.TotalInternalReflection);
                break;
            case "04-two-mirror-folded-path.staropt":
                Assert.Equal(3, Assert.Single(trace.Branches).Segments.Count);
                break;
            case "05-three-wavelength-sources.staropt":
                Assert.Equal(3, Assert.Single(trace.Detectors).PowerByWavelength.Count(item => item.Value.Sum() > 0));
                break;
            case "06-embedded-stl-baffle.staropt":
                var asset = Assert.Single(document.MeshAssets);
                Assert.True(asset.HasGeometry);
                Assert.Equal(8, asset.TriangleCount);
                Assert.False(asset.IsClosed);
                Assert.True(trace.EnergyBalance.AbsorbedPowerWatts > 0);
                break;
            case "07-ellipse-source-footprint.staropt":
            case "08-two-angle-anisotropic-source.staropt":
            case "09-radial-intensity-distribution.staropt":
            case "10-volume-rectangle-source.staropt":
            case "11-volume-ellipse-source.staropt":
            case "12-volume-cylinder-source.staropt":
                var source = Assert.Single(document.Objects, item => item.Parameters is SourceParameters);
                Assert.Equal(expected.SourceKind, source.Kind.ToString());
                Assert.Equal(2, trace.Detectors.Count);
                Assert.False(((DetectorRectangleParameters)document.Objects[1].Parameters).Absorb);
                Assert.True(((DetectorRectangleParameters)document.Objects[2].Parameters).Absorb);
                Assert.All(trace.Detectors, detector => Assert.True(detector.TotalPowerWatts > 0.99));
                break;
        }
    }

    private static SampleManifest ReadManifest(string directory) =>
        JsonSerializer.Deserialize<SampleManifest>(File.ReadAllText(Path.Combine(directory, "index.json")))
        ?? throw new InvalidDataException("Non-sequential teaching sample manifest is empty.");

    private static string SampleDirectory() =>
        Path.Combine(FindRepositoryRoot(), "samples", "non-sequential");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Unable to locate the repository root.");
    }

    private sealed record SampleManifest(
        int Version,
        string Name,
        string LengthUnit,
        IReadOnlyList<SampleManifestEntry> Samples);

    private sealed record SampleManifestEntry(
        string File,
        string Title,
        string Lesson,
        int ObjectCount,
        int MeshAssetCount,
        int BranchCount,
        double SourcePowerWatts,
        double DetectorPowerWatts,
        double AbsorbedPowerWatts,
        IReadOnlyList<string> SuggestedFilters,
        string? SourceKind,
        string? PreviewFile,
        IReadOnlyList<DetectorResult> DetectorResults);

    private sealed record DetectorResult(
        string Name,
        double PowerWatts,
        double PeakPixelPowerWatts,
        double CentroidXMillimeters,
        double CentroidYMillimeters,
        double RmsXMillimeters,
        double RmsYMillimeters);

    private sealed class DoubleArrayComparer(double tolerance) : IEqualityComparer<double[]>
    {
        public bool Equals(double[]? x, double[]? y)
        {
            if (x is null || y is null || x.Length != y.Length) return false;
            return x.Zip(y).All(pair => Math.Abs(pair.First - pair.Second) <= tolerance);
        }

        public int GetHashCode(double[] obj) => obj.Length;
    }
}
