using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class FirstOrderScalarContractTests
{
    private static readonly AnalysisComparisonEntry Entry = AnalysisComparisonRegistry.Get("First Order");
    private static readonly CanonicalAnalysisRequest Request = new()
    {
        CanonicalAnalysisKey = "First Order", Apodization = "Uniform", WorkbenchSettings = []
    };

    [Theory]
    [InlineData(1e10)]
    [InlineData(-1e10)]
    public void SignedNativeSaturationMatchesZeroPowerForPlaneOnlyOptics(double efl)
    {
        using var files = new FirstOrderFiles();
        files.WriteWorkbenchZeroPower();
        files.WriteNative(efl, "Tilted");
        var run = Compare(files);
        Assert.Equal(Conclusion.Pass, run.Conclusion);
        Assert.Equal(new[] { "EffectiveFocalLength:infinite-state", "FNumber:infinite-state" },
            run.Metrics.Select(metric => metric.Id));
        Assert.All(run.Metrics, metric => Assert.Equal(0, metric.MaxAbsolute));
    }

    [Fact]
    public void VerifiedInfinityAndFiniteFocalLengthAreDifferentStates()
    {
        using var files = new FirstOrderFiles();
        files.WriteWorkbenchZeroPower();
        files.WriteNative(500, "ChebyShv");
        var run = Compare(files);
        Assert.Equal(Conclusion.Difference, run.Conclusion);
        Assert.All(run.Metrics, metric => Assert.Equal(1, metric.MaxAbsolute));
    }

    [Fact]
    public void NativeMarkerAloneCannotCertifyOpaqueOrGridSurfacePower()
    {
        using var files = new FirstOrderFiles();
        foreach (var type in new[] { "UserDefined", "GridSag" })
        {
            files.WriteNative(1e10, type);
            var error = Assert.Throws<InvalidDataException>(() => ResultNormalizer.Zemax(files.Native, Entry, Request));
            Assert.Contains("could not be verified as zero power", error.Message);
        }
    }

    [Fact]
    public void WorkbenchInfinityNeedsExplicitZeroPowerState()
    {
        using var files = new FirstOrderFiles();
        JsonFiles.Write(files.Workbench, new AnalysisData("First Order", new Dictionary<string, object>
        {
            ["EffectiveFocalLength"] = double.PositiveInfinity,
            ["FNumber"] = double.PositiveInfinity
        }));
        Assert.Throws<InvalidDataException>(() => ResultNormalizer.Workbench(files.Workbench, Entry, Request));
    }

    private static AnalysisRun Compare(FirstOrderFiles files)
    {
        var actual = ResultNormalizer.Workbench(files.Workbench, Entry, Request);
        var expected = ResultNormalizer.Zemax(files.Native, Entry, Request);
        var run = new AnalysisRun { Key = "First Order", Directory = "first-order", Request = Request };
        ComparisonRunner.Compare(run, Entry, new AnalysisConfiguration { Quantities = new(Entry.DefaultTolerances) },
            actual, expected, files.Root);
        return run;
    }

    private sealed class FirstOrderFiles : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("first-order-").FullName;
        public string Workbench => Path.Combine(Root, "workbench.json");
        public string Native => Path.Combine(Root, "native.json");

        public void WriteWorkbenchZeroPower() => JsonFiles.Write(Workbench,
            new AnalysisData("First Order", new Dictionary<string, object>
            {
                ["EffectiveFocalLength"] = double.PositiveInfinity,
                ["FNumber"] = double.PositiveInfinity,
                ["EffectiveFocalLengthStatus"] = "Infinite",
                ["OpticalPowerInverseMillimeters"] = 0d,
                ["CardinalParametersApplicable"] = true
            }));

        public void WriteNative(double efl, string surfaceType)
        {
            JsonFiles.Write(Native, new
            {
                scalars = new Dictionary<string, double> { ["EffectiveFocalLength"] = efl, ["FNumber"] = Math.Abs(efl) / 10 },
                operands = new Dictionary<string, double> { ["EFFL"] = efl, ["EPDI"] = 10 }
            });
            JsonFiles.Write(Path.Combine(Root, "model.json"), new
            {
                surfaces = new[] { "Standard", surfaceType, "Standard" }
                    .Select(type => new Dictionary<string, object?> { ["Type"] = type, ["Radius"] = null })
            });
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
