using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class CoatingWorkflowTests
{
    [Fact]
    public async Task ExplicitReferenceRoundTripsIndependentlyWithoutChangingOldPhysicalIdentity()
    {
        var d = Examples.Create(DesignKind.HighReflector); var generated = new DesignService().Generate(d);
        var changed = generated with { ReferenceWavelengthNm = 630 };
        Assert.Equal(generated.Fingerprint(), changed.Fingerprint());
        Assert.Equal(generated.Result, changed.Result);
        Assert.DoesNotContain("ReferenceWavelengthNm", JsonSerializer.Serialize(d, Experiment.JsonOptions));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try { await ExperimentStore.SaveAsync(changed, path); var reopened = await ExperimentStore.OpenAsync(path); Assert.Equal(630, reopened.ReferenceWavelengthNm); Assert.Equal(generated.Layers, reopened.Layers); }
        finally { File.Delete(path); }
    }
    [Fact]
    public void FormulaUsesExplicitReferenceAndRealMaterialSnapshotWithBoundedNestedExpansion()
    {
        var d = Examples.Create(DesignKind.HighReflector) with { ReferenceWavelengthNm = 625 };
        var parsed = CoatingStackFormula.Parse(d, false, "Inc | 0.5(HL)^2 2H | Sub");
        Assert.Equal(5, parsed.Layers.Length); Assert.Equal(625, parsed.ReferenceWavelengthNm);
        var registry = new MaterialRegistry();
        var expected = new[] { .5, .5, .5, .5, 2 };
        for (var i = 0; i < expected.Length; i++)
        {
            var l = parsed.Layers[i]; var m = d.Materials.Single(m => m.Id == l.MaterialId).Resolve(registry);
            Assert.Equal(expected[i], 4 * m.RefractiveIndex(625) * l.ThicknessNm / 625, 12);
        }
        Assert.Equal(parsed.Layers, CoatingStackFormula.Parse(d, false, "0.5(Hi Lo)^2 2Hi", new Dictionary<string, string> { ["Hi"] = d.HighId, ["Lo"] = d.LowId }).Layers);
        var reverse = DuplexCoatingTests.Design(); Assert.Single(CoatingStackFormula.Parse(reverse, true, "{ " + reverse.HighId + " }").Layers);
    }
    [Theory]
    [InlineData("(HL)^101")]
    [InlineData("(HL)^0")]
    [InlineData("0H")]
    [InlineData("-1H")]
    [InlineData("NaNH")]
    [InlineData("H; System.Run()")]
    [InlineData("(H")]
    [InlineData("()")]
    [InlineData("Sub | HL | Inc")]
    public void InvalidFormulaNeverReturnsPartialLayers(string formula) => Assert.ThrowsAny<ArgumentException>(() => CoatingStackFormula.Parse(Examples.Create(DesignKind.HighReflector), false, formula));
    [Fact]
    public void FormulaDomainNestingAndAliasesAreBoundedWithoutGuessingMaterials()
    {
        var d = Examples.Create(DesignKind.HighReflector);
        Assert.Equal(200, CoatingStackFormula.Parse(d, false, "(HL)^100").Layers.Length);
        Assert.Throws<ArgumentException>(() => CoatingStackFormula.Parse(d, false, new string('(', 17) + "H" + new string(')', 17)));
        Assert.Throws<ArgumentException>(() => CoatingStackFormula.Parse(d, false, "H", Enumerable.Range(0, 65).ToDictionary(i => "M" + i, _ => d.HighId)));
        var strict = MaterialLibrary.Capture(new ConstantIndexMaterial("strict known film", 2), "declared test domain", 500, 600);
        d = d with { Materials = [.. d.Materials, strict], HighId = strict.Id, ReferenceWavelengthNm = 650 };
        Assert.Throws<ArgumentException>(() => CoatingStackFormula.Parse(d, false, "H"));
        Assert.Throws<ArgumentException>(() => CoatingStackFormula.Parse(d, false, "H", new Dictionary<string, string> { ["H"] = "unknown material" }));
    }
    [Fact]
    public void RichClipboardPreservesFullPrecisionAndRemapsConflictingMaterialWithoutOverwritingNk()
    {
        var d = DuplexCoatingTests.Design(); var layer = new FilmLayer(d.LowId, 73.1234567891234, false);
        var original = d.Materials.Single(m => m.Id == d.LowId);
        var different = MaterialLibrary.Capture(new ConstantIndexMaterial(original.Name, 2.3, .04), "different source", 450, 750) with { Id = d.LowId };
        var receiver = d with { Materials = d.Materials.Select(m => m.Id == d.LowId ? different : m).ToArray(), ReferenceWavelengthNm = 700 };
        var packet = CoatingLayerClipboard.Encode(d, [layer]); var pasted = CoatingLayerClipboard.Decode(receiver, packet, true);
        Assert.StartsWith("clipboard-", pasted.Layers[0].MaterialId); Assert.Equal(layer.ThicknessNm, pasted.Layers[0].ThicknessNm); Assert.False(pasted.Layers[0].Variable);
        Assert.Equal(different, pasted.Materials.Single(m => m.Id == different.Id));
        var material = pasted.Materials.Single(m => m.Id == pasted.Layers[0].MaterialId);
        Assert.Equal(JsonSerializer.Serialize(original.Model), JsonSerializer.Serialize(material.Model));
        var twice = CoatingLayerClipboard.Decode(receiver with { Materials = pasted.Materials }, packet, true);
        Assert.Equal(pasted.Materials.Length, twice.Materials.Length); Assert.Equal(pasted.Layers, twice.Layers);
        var plain = CoatingLayerClipboard.Decode(d, CoatingLayerClipboard.PlainText([layer]), false);
        Assert.Equal(layer, plain.Layers.Single());
        Assert.ThrowsAny<Exception>(() => CoatingLayerClipboard.Decode(d, packet.Replace("exterior-to-substrate", "reverse"), true));
        Assert.ThrowsAny<Exception>(() => CoatingLayerClipboard.Decode(d, "unknown\t100", false));
        Assert.ThrowsAny<Exception>(() => CoatingLayerClipboard.Decode(d, d.LowId + "\tNaN", false));
    }
    [Fact]
    public async Task RecoveryLeasesSeparateWindowsKeepLatestDraftAndPreserveCorruption()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coating-recovery-test-" + Guid.NewGuid());
        try
        {
            string path;
            using (var live = CoatingRecoverySession.Create(dir))
            using (var other = CoatingRecoverySession.Create(dir))
            {
                path = live.Path;
                await live.WriteAsync("raw", "{\"thickness\":\"unfinished.\"}", 3);
                await live.WriteAsync("old", "{}", 1);
                Assert.Contains("unfinished.", live.Read().EditorJson);
                await other.WriteAsync("peer", "{}", 1);
                Assert.Empty(CoatingRecoverySession.AcquireAvailable(dir));
                await other.CompleteAsync(); Assert.True(File.Exists(live.Path)); Assert.False(File.Exists(other.Path));
            }
            var drafts = CoatingRecoverySession.AcquireAvailable(dir);
            try { var acquired = Assert.Single(drafts); Assert.Equal(path, acquired.Path); Assert.Contains("unfinished.", acquired.Read().EditorJson); }
            finally { foreach (var s in drafts) s.Dispose(); }
            var content = await File.ReadAllTextAsync(path); await File.WriteAllTextAsync(path, content.Replace("unfinished.", "tampered"));
            var damaged = Assert.Single(CoatingRecoverySession.AcquireAvailable(dir));
            using (damaged) Assert.Throws<InvalidDataException>(() => damaged.Read());
            Assert.True(File.Exists(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task CompletedRecoveryCannotBeRecreatedByQueuedOldWritesAndClearHonorsRevision()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coating-recovery-race-" + Guid.NewGuid());
        try
        {
            using var s = CoatingRecoverySession.Create(dir);
            await Task.WhenAll(s.WriteAsync("new", "{\"latest\":true}", 10), s.WriteAsync("old", "{}", 2));
            Assert.Contains("latest", s.Read().EditorJson);
            await s.ClearAsync(3); Assert.True(File.Exists(s.Path));
            await s.ClearAsync(11); Assert.False(File.Exists(s.Path));
            await s.WriteAsync("stale", "{}", 9); Assert.False(File.Exists(s.Path));
            var pending = s.WriteAsync("new", "{}", 12); var close = s.CompleteAsync();
            await Task.WhenAll(pending, close); await s.WriteAsync("too late", "{}", 13); Assert.False(File.Exists(s.Path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
