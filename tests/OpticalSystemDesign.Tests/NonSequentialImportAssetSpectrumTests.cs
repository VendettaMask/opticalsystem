using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialImportAssetSpectrumTests
{
    private const string Header = "MODE NSC\nUNIT MM X W\nWAVM 1 .55 1\nPWAV 1\nSURF 1\nTYPE NONSEQCO\n";
    private const string Ray = "NSOH NSC_SRAY 0 0\nNSOP 0 0 -10 0 0 0\nNSOD 1 1\nNSOD 2 1\nNSOD 3 1\nNSOD 4 1\nNSOD 5 0\nNSOD 6 0\nNSOD 7 0\nNSOD 8 1\nNSOD 9 0\n";
    private const string Detector = "NSOH NSC_DETE 0 0\nNSOP 0 0 20 0 0 0 ABSORB\nNSOD 1 10\nNSOD 2 10\nNSOD 3 8\nNSOD 4 8\n";
    private const string Cube = """
        V 1 -1 -1 0
        V 2 1 -1 0
        V 3 1 1 0
        V 4 -1 1 0
        V 5 -1 -1 2
        V 6 1 -1 2
        V 7 1 1 2
        V 8 -1 1 2
        R 1 2 3 4 0 0
        R 5 6 7 8 0 2
        R 4 3 7 8 0 3
        R 1 2 6 5 0 3
        R 1 4 8 5 0 3
        R 2 3 7 6 0 3
        """;

    [Fact]
    public void ReferencedSourceAssetsAreLocatedInTheObjectsDataTreeWithoutEnablingUnknownModels()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var root = files.Path("Data"); var sources = Path.Combine(root, "Objects", "Sources", "IESNA"); Directory.CreateDirectory(sources);
        File.WriteAllText(Path.Combine(sources, "fixture.ies"), "IESNA fixture");
        var samples = Path.Combine(root, "Samples", "Non-sequential"); Directory.CreateDirectory(samples);
        var source = Path.Combine(samples, "source.zmx");
        File.WriteAllText(source, Header + "NSOH NSC_SIES 0 0 fixture.ies\nNSOP 0 0 0 0 0 0\n");
        var result = ZemaxNonSequentialReader.Read(source);
        Assert.True(Assert.Single(result.Assets).Exists); Assert.DoesNotContain(result.Diagnostics, d => d.Code == "MissingAsset");
        Assert.False(result.CanApply); Assert.Contains(result.Diagnostics, d => d.Code == "ObjectType");
    }

    [Fact]
    public async Task PolygonResourcesResolveFromDataFoldersEmbedExactFacesAndSurviveSourceRemoval()
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var data = files.Path("Data"); var assets = Path.Combine(data, "Objects", "Polygon Objects"); Directory.CreateDirectory(assets);
        var sample = Path.Combine(data, "Samples", "Non-sequential", "test"); Directory.CreateDirectory(sample);
        var asset = Path.Combine(assets, "cube.pob"); File.WriteAllText(asset, Cube);
        var source = Path.Combine(sample, "cube.zmx");
        File.WriteAllText(source, Header + Ray + "NSOH NSC_POBJ 0 0 CUBE.POB\nNSOP 0 0 0 0 0 0 N-BK7\nNSOD 1 2\nNSOD 2 1\n" + Detector);
        var imported = ZemaxNonSequentialReader.Read(source); Assert.True(imported.CanApply, Describe(imported));
        Assert.True(Assert.Single(imported.Assets).Exists);
        var doc = imported.Document!; var mesh = Assert.Single(doc.MeshAssets);
        Assert.True(mesh.IsClosed); Assert.Equal(4, mesh.BoundsMaximum.Z, 11);
        var trace = new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials);
        var hits = trace.Branches.SelectMany(b => b.Segments).Where(s => s.ObjectId == doc.Objects[1].Id).ToArray();
        Assert.Equal(0, hits[0].End.Z, 11); Assert.Contains(hits, h => Math.Abs(h.End.Z - 4) < 1e-10 && h.FaceNumber == 3);
        Assert.True(trace.Detectors.Single().TotalPowerWatts > 0);
        File.Delete(asset);
        var native = files.Path("polygon.staropt"); await StarOptProjectStore.SaveAsync(new([Optic.CreateBlank()], 0, NonSequentialDocument: doc), native);
        var restored = (await StarOptProjectStore.LoadAsync(native)).NonSequentialDocument!;
        Assert.True(Assert.Single(restored.MeshAssets).HasGeometry);
        var again = new NonSequentialDocumentTracer().Trace(restored, Optic.CreateBlank().Materials);
        Assert.Equal(trace.Detectors.Single().TotalPowerWatts, again.Detectors.Single().TotalPowerWatts, 12);
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("missing-vertex")]
    [InlineData("open-volume")]
    public void InvalidPolygonResourcesDoNotPublishPartialScenes(string fault)
    {
        using var files = new CameraSensorFileTests.TemporaryFiles();
        var payload = fault switch
        {
            "mixed" => Cube.Replace("R 1 2 3 4 0", "R 1 2 3 4 1"),
            "missing-vertex" => Cube.Replace("R 1 2 3 4", "R 99 2 3 4"),
            _ => Cube.Replace("R 1 2 3 4 0 0", "")
        };
        File.WriteAllText(files.Path("cube.pob"), payload);
        var source = files.Path("source.zmx"); File.WriteAllText(source, Header + "NSOH NSC_POBJ 0 0 cube.pob\nNSOP 0 0 0 0 0 0 N-BK7\nNSOD 1 1\nNSOD 2 1\n");
        var result = ZemaxNonSequentialReader.Read(source); Assert.False(result.CanApply); Assert.Null(result.Document);
        Assert.Contains(result.Diagnostics, d => d.Blocking && d.Code == "ObjectDefinition");
    }

    [Theory]
    [InlineData("NSC_CBLK", "NSOD 1 2\nNSOD 2 4\nNSOD 3 3\n")]
    [InlineData("NSC_SLEN", "NSOD 1 0\nNSOD 2 0\nNSOD 3 2\nNSOD 4 2\nNSOD 5 4\nNSOD 6 0\nNSOD 7 0\nNSOD 8 3\nNSOD 9 3\n")]
    public void TaperedVolumesUseExactCapsAndRuledSides(string type, string parameters)
    {
        var result = ZemaxNonSequentialReader.Parse(Header + Ray + $"NSOH {type} 0 0\nNSOP 0 0 0 0 0 0 N-BK7\n" + parameters + Detector);
        Assert.True(result.CanApply, Describe(result));
        var doc = result.Document!; var group = Assert.IsType<OpticalAssemblyParameters>(doc.Objects[1].Parameters);
        Assert.Equal(2, group.Interfaces[0].MechanicalSemiDiameterMillimeters); Assert.Equal(3, group.Interfaces[1].MechanicalSemiDiameterMillimeters);
        var query = NonSequentialDocumentTracer.CompileIntersections(doc);
        var hit = query.IntersectObject(doc.Objects[1].Id, new(new(5, 0, 2), new(-1, 0, 0), 550));
        Assert.NotNull(hit); Assert.Equal(2.5, hit.Position.X, 11); Assert.Equal(2, hit.Position.Z, 11);
        Assert.True(new NonSequentialDocumentTracer().Trace(doc, Optic.CreateBlank().Materials).Detectors.Single().TotalPowerWatts > 0);
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(10)]
    [InlineData(1e9)]
    public void BlackbodyDiscreteWeightsStayFiniteAndUseWavelengthRadiance(double temperature)
    {
        var result = NonSequentialColorimetry.SampleSpectrum(400, 800, 3, temperature);
        Assert.All(result, s => Assert.True(double.IsFinite(s.RelativePower) && s.RelativePower >= 0));
        Assert.Equal(1, result.Sum(s => s.RelativePower), 12);
        if (temperature == 5000)
        {
            var c2 = 6.62607015e-34 * 299792458 / 1.380649e-23 * 1e9;
            var ratio = Math.Pow(800d / 400, 5) * (Math.Exp(c2 / 800 / temperature) - 1) / (Math.Exp(c2 / 400 / temperature) - 1);
            Assert.Equal(ratio, result[0].RelativePower / result[^1].RelativePower, 12);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    public void ExplicitSourceSpectrumOverridesTheSystemWavelengthAndDrivesActualDetectorBands(int mode)
    {
        var source = "NSOH NSC_SRCR 0 0\nNSOP 0 0 0 0 0 0\nNSOD 1 10\nNSOD 2 1000\nNSOD 3 1\nNSOD 4 1\nNSOD 5 0\nNSOD 6 1\nNSOD 7 1\n";
        var result = ZemaxNonSequentialReader.Parse(Header + source + $"NSCS 7 3 {mode} 5000 0 0 .5 .7 3 \"\"\n" + Detector);
        Assert.True(result.CanApply, Describe(result));
        var emitter = (SourceParameters)result.Document!.Objects[0].Parameters;
        Assert.Equal(0, emitter.WavelengthNumber); Assert.Equal(3, emitter.Physics!.Spectrum!.Count);
        var frame = new NonSequentialDocumentTracer().Trace(result.Document, Optic.CreateBlank().Materials, new(RayCountOverride: 300)).Detectors.Single();
        Assert.Equal(1, frame.TotalPowerWatts, 11);
        Assert.True(!frame.PowerByWavelength.TryGetValue(1, out var original) || original.Sum() == 0);
    }

    [Fact]
    public void ChromaticityAndD65ModelsRetainTheirRequestedColor()
    {
        foreach (var (mode, a, b) in new[] { (2, .3127, .329), (5, 0d, 0d), (9, .19783, .46832) })
        {
            var definition = FormattableString.Invariant($"NSCS 7 3 {mode} {a} {b} 0 .44 .64 20 \"\"\n");
            var result = ZemaxNonSequentialReader.Parse(Header + Ray + definition + Detector);
            Assert.True(result.CanApply, Describe(result));
            var spectrum = ((SourceParameters)result.Document!.Objects[0].Parameters).Physics!.Spectrum!;
            var xyz = spectrum.Select(s =>
            {
                var response = NonSequentialColorimetry.Response(s.Nanometers); return
                (X: response.X * s.RelativePower, Y: response.Y * s.RelativePower, Z: response.Z * s.RelativePower);
            }).ToArray();
            var total = xyz.Sum(v => v.X + v.Y + v.Z);
            var expectedX = mode == 5 ? .9505 / (.9505 + 1 + 1.089) : mode == 2 ? a : 9 * a / (6 * a - 16 * b + 12);
            var expectedY = mode == 5 ? 1 / (.9505 + 1 + 1.089) : mode == 2 ? b : 4 * b / (6 * a - 16 * b + 12);
            Assert.Equal(expectedX, xyz.Sum(v => v.X) / total, 7); Assert.Equal(expectedY, xyz.Sum(v => v.Y) / total, 7);
        }
    }

    [Fact]
    public void LumenSourcesConvertThroughTheSelectedSpectrumAndRejectInvisiblePower()
    {
        var input = Header.Replace("UNIT MM X W", "UNIT MM X L").Replace(".55 1", ".555 1") + Ray.Replace("NSOD 3 1", "NSOD 3 683") + Detector;
        var result = ZemaxNonSequentialReader.Parse(input); Assert.True(result.CanApply, Describe(result));
        Assert.Equal(1, ((SourceParameters)result.Document!.Objects[0].Parameters).PowerWatts, 12);
        Assert.Equal(1, new NonSequentialDocumentTracer().Trace(result.Document, Optic.CreateBlank().Materials).Detectors.Single().TotalPowerWatts, 12);
        var invisible = ZemaxNonSequentialReader.Parse(input.Replace(".555 1", "1.2 1"));
        Assert.False(invisible.CanApply); Assert.Null(invisible.Document);
    }

    private static string Describe(ZemaxNscImportResult result) => string.Join("\n", result.Diagnostics.Select(d => d.Message));
}
