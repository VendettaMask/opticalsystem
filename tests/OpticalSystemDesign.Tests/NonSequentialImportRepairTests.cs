using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialImportRepairTests
{
    private const string Header = "MODE NSC\nUNIT MM\nWAVM 1 .55 1\nPWAV 1\nSURF 1\nTYPE NONSEQCO\n";
    private const string Ray = "NSOH NSC_SRAY 0 0 test ray\nNSOP 0 0 -10 0 0 0\nNSOD 1 1\nNSOD 2 1\nNSOD 3 1\nNSOD 4 1\nNSOD 5 0\nNSOD 6 0\nNSOD 7 0\nNSOD 8 1\nNSOD 9 0\n";
    private const string Detector = "NSOH NSC_DETE 0 0 result\nNSOP 0 0 20 0 0 0 ABSORB\nNSOD 1 5\nNSOD 2 5\nNSOD 3 4\nNSOD 4 4\n";

    [Fact]
    public async Task RepairedBundledScenesTraceWithFunctionalSamplingAndReopenTheirNativeSnapshots()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var root = Path.GetDirectoryName(Path.GetDirectoryName(NonSequentialColorFringesTests.Source))!;
        foreach (var relative in new[] { "Sources/sobol sampling.ZMX", "Miscellaneous/Even asphere.zmx",
            "Colorimetry/Example 1  Two-color mixing gives white.ZMX" })
        {
            var result = ZemaxNonSequentialReader.Read(Path.Combine(root, relative));
            Assert.True(result.CanApply, relative + "\n" + Describe(result));
            var trace = new NonSequentialDocumentTracer().Trace(result.Document!, Optic.CreateBlank().Materials,
                new(RayCountOverride: 64));
            Assert.NotEmpty(trace.Detectors); Assert.True(trace.Detectors.Any(d => d.TotalPowerWatts > 0), relative);
            var native = files.Path(Path.GetFileName(relative) + ".staropt");
            await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()], 0, NonSequentialDocument: result.Document), native);
            var restored = await StarOptProjectStore.LoadAsync(native);
            Assert.Equal(result.Document!.Objects.Count, restored.NonSequentialDocument!.Objects.Count);
        }
    }

    [Fact]
    public void CommentsRelativeReferencesAndPointSourcesPreserveWorldCoordinatesAndPower()
    {
        var result = Import("NSOH NSC_NULL 0 0 tilted frame\nNSOP 3 4 10 0 0 90\n"
            + "NSOH NSC_SRCP -1 0 point source\nNSOP 1 0 0 0 0 0\nNSOD 1 2\nNSOD 2 4\nNSOD 3 1\nNSOD 4 1\nNSOD 5 0\nNSOD 6 0\n"
            + Detector.Replace("NSC_DETE 0 0", "NSC_DETE -2 0").Replace("NSOP 0 0 20", "NSOP 1 0 20"));
        var doc = result.Document!;
        Assert.Equal("point source", doc.Objects[1].Name);
        Assert.Equal(doc.Objects[0].Id, doc.Objects[1].ReferenceObjectId);
        var position = doc.ToWorldPoint(doc.Objects[1].Id, new(0, 0, 0));
        Assert.Equal(3, position.X, 11); Assert.Equal(5, position.Y, 11); Assert.Equal(10, position.Z, 11);
        var trace = new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials);
        Assert.Equal(1, trace.Detectors.Single().TotalPowerWatts, 11);
    }

    [Fact]
    public void ParameterPickupColumnZeroUsesTheSameParameterInsteadOfItsStaleCache()
    {
        var first = "NSOH NSC_RECT 0 0 first\nNSOP 0 0 0 0 0 0 MIRROR\nNSOD 1 3\nNSOD 2 4\n";
        var second = "NSOH NSC_RECT 0 0 second\nNSOP 0 0 10 0 0 0 MIRROR\nNSOD 1 999 2 1 2 1 0\nNSOD 2 4\n";
        var result = Import(first + second);
        Assert.Equal(14, ((PlaneRectangleParameters)result.Document!.Objects[1].Parameters).WidthMillimeters);
        var cycle = ZemaxNonSequentialReader.Parse(Header + first.Replace("NSOD 1 3", "NSOD 1 3 2 2 1 0 0") + second);
        Assert.False(cycle.CanApply); Assert.Contains(cycle.Diagnostics, d => d.Message.Contains("循环"));
    }

    [Fact]
    public void VariableCellsImportCurrentValuesAndKeepTheOriginalFlags()
    {
        var input = Header + Ray.Replace("NSOD 3 1", "NSOD 3 2 1")
            .Replace("NSOP 0 0 -10 0 0 0", "NSOP 0 0 -10 0 0 0\nNSOV 0 0 1 0 0 0 0") + Detector;
        var result = ZemaxNonSequentialReader.Parse(input);
        Assert.True(result.CanApply, Describe(result));
        Assert.Contains(result.Diagnostics, d => d.Code == "VariableSnapshot" && !d.Blocking);
        Assert.Equal(2, ((SourceRayParameters)result.Document!.Objects[0].Parameters).PowerWatts);
        Assert.Contains(result.Objects[0].Records, r => r.Keyword == "NSOD" && r.Values[0] == "3" && r.Values[2] == "1");
    }

    [Theory]
    [InlineData("NSC_SPHE", "NSOD 1 2\nNSOD 2 1\n", -2)]
    [InlineData("NSC_CBLK", "NSOD 1 2\nNSOD 2 3\nNSOD 3 2\n", 0)]
    [InlineData("NSC_SLEN", "NSOD 1 0\nNSOD 2 0\nNSOD 3 2\nNSOD 4 2\nNSOD 5 3\nNSOD 6 0\nNSOD 7 0\nNSOD 8 2\nNSOD 9 2\n", 0)]
    public void ExistingAnalyticShapesUseNativeVerticesAndCatalogMaterials(string type, string parameters, double firstZ)
    {
        var result = Import(Ray + $"NSOH {type} 0 0 body\nNSOP 0 0 0 0 0 0 N-BK7\nNSCS 11 2 64 32 0\n" + parameters + Detector);
        var doc = result.Document!;
        var trace = new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials);
        var segments = trace.Branches.SelectMany(b => b.Segments).Where(s => s.ObjectId == doc.Objects[1].Id).ToArray();
        Assert.NotEmpty(segments); Assert.Equal(firstZ, segments[0].End.Z, 10);
        Assert.True(trace.Detectors.Single().TotalPowerWatts > 0);
    }

    [Theory]
    [InlineData("ABSORB", NonSequentialSurfaceBehavior.Absorbing)]
    [InlineData("MIRROR", NonSequentialSurfaceBehavior.Reflective)]
    public void RectangularVolumesPreserveAbsorberAndMirrorBehavior(string material, NonSequentialSurfaceBehavior expected)
    {
        var result = Import(Ray + $"NSOH NSC_RBLK 0 0 body\nNSOP 0 0 0 0 0 0 {material}\n"
            + "NSOD 1 2\nNSOD 2 2\nNSOD 3 3\nNSOD 4 2\nNSOD 5 2\nNSOD 6 0\nNSOD 7 0\nNSOD 8 0\nNSOD 9 0\n" + Detector);
        Assert.Equal(expected, ((MeshObjectParameters)result.Document!.Objects[1].Parameters).Behavior);
        var trace = new NonSequentialDocumentTracer().Trace(result.Document, Optic.CreateBlank().Materials);
        Assert.Equal(0, trace.Detectors.Single().TotalPowerWatts);
    }

    [Fact]
    public void OrdinaryRectangularDetectorsRetainSupportedPolarizationComponents()
    {
        var input = Header + "NSCD 100 500 0 0.001 10 0.000001 1 1 0 0 0 1 1000000 0 2\n" + Ray + Detector + "NSOD 16 1\n";
        var result = ZemaxNonSequentialReader.Parse(input);
        Assert.True(result.CanApply, Describe(result));
        Assert.Equal(1, ((DetectorRectangleParameters)result.Document!.Objects[1].Parameters).PolarizationComponent);
        var trace = new NonSequentialDocumentTracer().Trace(result.Document, Optic.CreateBlank().Materials);
        Assert.Equal(.5, trace.Detectors.Single().TotalPowerWatts, 11);
    }

    [Fact]
    public void ObjectHeaderAssetsAndInvalidReferencesAreDiagnosedWithoutPartialScenePublication()
    {
        var missing = ZemaxNonSequentialReader.Parse(Header + "NSOH NSC_POBJ 0 0 \"missing shape.pob\"\nNSOP 0 0 0 0 0 0\n");
        Assert.False(missing.CanApply); Assert.Contains(missing.Assets, a => a.Reference == "missing shape.pob" && !a.Exists);
        Assert.Contains(missing.Diagnostics, d => d.Code == "MissingAsset");
        var invalid = ZemaxNonSequentialReader.Parse(Header + Ray + Detector.Replace("NSC_DETE 0 0", "NSC_DETE 2 0"));
        Assert.False(invalid.CanApply); Assert.Null(invalid.Document);
        var unknownPhysics = ZemaxNonSequentialReader.Parse(Header + Ray + Detector + "NSCS 1 1 UNKNOWN_COATING\n");
        Assert.False(unknownPhysics.CanApply); Assert.Contains(unknownPhysics.Diagnostics, d => d.Code == "UnmappedObjectRecord");
    }

    private static ZemaxNscImportResult Import(string objects)
    {
        var result = ZemaxNonSequentialReader.Parse(Header + objects);
        Assert.True(result.CanApply, Describe(result)); return result;
    }
    private static string Describe(ZemaxNscImportResult result) => string.Join("\n", result.Diagnostics.Select(d => d.Message));
}
