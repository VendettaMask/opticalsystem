using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialResourceLayoutTests
{
    private static string Root => Path.Combine(NonSequentialColorFringesTests.Root, "src", "OpticalSystemDesign.App", "Assets", "NonSequentialExamples");
    private const string Header = "MODE NSC\nUNIT MM\nWAVM 1 .55 1\nPWAV 1\nSURF 1\nTYPE NONSEQCO\n";
    private const string Ray = "NSOH NSC_SRAY 0 0\nNSOP 0 0 -10 0 0 0\nNSOD 1 1\nNSOD 2 1\nNSOD 3 1\nNSOD 4 1\nNSOD 5 0\nNSOD 6 0\nNSOD 7 0\nNSOD 8 1\nNSOD 9 0\n";

    [Fact]
    public void BundledResourceTreeKeepsAllOriginalPathsHashesAndBuildOutputBytes()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "asset-index.json")));
        var assets = manifest.RootElement.GetProperty("Assets").EnumerateArray().ToArray();
        Assert.NotEmpty(assets);
        Assert.Equal(assets.Length, assets.Select(a => a.GetProperty("RelativePath").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var entry in assets)
        {
            var relative = entry.GetProperty("RelativePath").GetString()!;
            Assert.StartsWith("Objects/", relative);
            var source = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            var output = Path.Combine(AppContext.BaseDirectory, "Examples", "NonSequential", relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(entry.GetProperty("Sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))));
            Assert.Equal(entry.GetProperty("Bytes").GetInt64(), new FileInfo(output).Length);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(output));
        }
    }

    [Fact]
    public void AllSuppliedReferencesResolveAlongsideThePackagedExamples()
    {
        var source = Path.Combine(Root, "Objects", "Polygon Objects", "Prism45.pob");
        Assert.True(File.Exists(source));
        var original = Path.Combine(Root, "Birefringence", "Wollaston prism using non-sequential ray-tracing.zmx");
        var result = ZemaxNonSequentialReader.Read(original);
        Assert.Contains(result.Assets, a => a.Reference.Equals("PRISM45.POB", StringComparison.OrdinalIgnoreCase) && a.Exists);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "MissingAsset");
    }

    [Fact]
    public async Task OriginalPrismResourceTracesAndReopensFromAnIndependentProjectTree()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var data = files.Path("portable-data"); var objects = Path.Combine(data, "Objects", "Polygon Objects"); Directory.CreateDirectory(objects);
        var designs = Path.Combine(data, "Designs", "Prisms"); Directory.CreateDirectory(designs);
        var resource = Path.Combine(objects, "Prism45.pob");
        File.Copy(Path.Combine(Root, "Objects", "Polygon Objects", "Prism45.pob"), resource);
        var zmx = Path.Combine(designs, "fresh-design.zmx");
        File.WriteAllText(zmx, Header + Ray + "NSOH NSC_POBJ 0 0 PRISM45.POB\nNSOP 0 0 0 0 0 0 N-BK7\nNSOD 1 1\nNSOD 2 1\n");
        var imported = ZemaxNonSequentialReader.Read(zmx);
        Assert.True(imported.CanApply, string.Join("\n", imported.Diagnostics.Select(d => d.Message)));
        var doc = imported.Document!; Assert.True(Assert.Single(doc.MeshAssets).IsClosed);
        var trace = new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials);
        var hits = trace.Branches.SelectMany(b => b.Segments).Where(s => s.ObjectId == doc.Objects[1].Id).ToArray();
        Assert.NotEmpty(hits); Assert.Equal(0, hits[0].End.Z, 11);
        Assert.Contains(hits, h => h.InteractionKind == OpticalSystemDesign.Core.Interactions.RayInteractionKind.TotalInternalReflection);
        File.Delete(resource);
        var native = files.Path("prism.staropt"); await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()], 0, NonSequentialDocument: doc), native);
        var restored = (await StarOptProjectStore.LoadAsync(native)).NonSequentialDocument!;
        var replay = new NonSequentialDocumentTracer().Trace(restored, Optic.CreateBlank().Materials);
        Assert.Equal(trace.Branches.SelectMany(b => b.Segments).Select(s => s.End), replay.Branches.SelectMany(b => b.Segments).Select(s => s.End));
    }

    [Theory]
    [InlineData("Triangle.POB")]
    [InlineData("Square.POB")]
    [InlineData("Pentagon.POB")]
    [InlineData("Hexagon.POB")]
    public void LegacyOpenPolygonResourcesAcceptOmittedDefaultInteractionFlags(string resourceName)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var directory = Path.GetDirectoryName(files.Path("file.zmx"))!;
        var objects = Path.Combine(directory, "Objects", "Polygon Objects"); Directory.CreateDirectory(objects);
        File.Copy(Path.Combine(Root, "Objects", "Polygon Objects", resourceName), Path.Combine(objects, resourceName));
        var source = files.Path("new-design.zmx");
        File.WriteAllText(source, Header + $"NSOH NSC_POBJ 0 0 {resourceName}\nNSOP 0 0 0 0 0 0 MIRROR\nNSOD 1 1\nNSOD 2 0\n");
        var result = ZemaxNonSequentialReader.Read(source);
        Assert.True(result.CanApply, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.False(Assert.Single(result.Document!.MeshAssets).IsClosed);
        Assert.Equal(NonSequentialSurfaceBehavior.Reflective, ((MeshObjectParameters)result.Document.Objects[0].Parameters).Behavior);
    }
}
