using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Application.Services;
using Dto = OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.Tests;

public sealed class NonSequentialDesignArchiveTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CompleteArchiveReplaysOptimizationOrManufacturingWithoutOriginalCatalogues(bool tolerancing)
    {
        var optic = Optic.CreateBlank(); optic.Materials.Register(new ConstantIndexMaterial("Independent medium", 1.25));
        optic.Materials.Register("custom alias", optic.Materials.Resolve("Independent medium"));
        var source = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with { LocalCoordinateSystem = new(new(.3, 0, 0)) };
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
            { LocalCoordinateSystem = new(new(0, 0, 1)), Parameters = new DetectorRectangleParameters(4, 4, 2, 2) };
        var doc = new NonSequentialDocument("independent", [new("d", 550, 1, true)], [source, detector], "custom alias");
        var variable = new NonSequentialDesignVariable(source.Id, NonSequentialVariableDomain.Position, "X", -1, 1, .02);
        var target = new NonSequentialDesignTarget(detector.Id, NonSequentialMetric.CentroidX, 0, .01);
        var settings = new NonSequentialDesignSettings(MaximumTraceEvaluations: 100);
        var plan = new NonSequentialDesignExperiment(1, NonSequentialSceneHasher.Compute(doc, optic.Materials), [variable], [target], settings,
            [new(variable, .05, NonSequentialToleranceDistribution.Uniform)], new(12, 42, settings), []);
        var optimization = tolerancing ? null : NonSequentialDesignEngine.Optimize(doc, optic.Materials, plan.Variables, plan.Targets, settings);
        var tolerance = !tolerancing ? null : NonSequentialToleranceEngine.Run(doc, optic.Materials, plan.Tolerances!, plan.Targets, 12, 42, settings);
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("independent.nsc-experiment.zip");
        await NonSequentialDesignArchiveStore.SaveAsync(path, new([optic], 0, NonSequentialDocument: doc), optic.Materials, plan,
            tolerancing ? NonSequentialDesignRunKind.Tolerance : NonSequentialDesignRunKind.Optimization, optimization, tolerance);
        var copy = files.Path("identical.zip");
        await NonSequentialDesignArchiveStore.SaveAsync(copy, new([optic], 0, NonSequentialDocument: doc), optic.Materials, plan,
            tolerancing ? NonSequentialDesignRunKind.Tolerance : NonSequentialDesignRunKind.Optimization, optimization, tolerance);
        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(copy));
        using (var stream = File.OpenRead(path)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            Assert.Equal(5, zip.Entries.Count); Assert.Single(zip.Entries, e => e.FullName == "source.staropt");
            using var input = zip.GetEntry("result.json")!.Open(); using var json = await JsonDocument.ParseAsync(input);
            Assert.DoesNotContain("Candidate", json.RootElement.GetRawText()); // Scene and assets have one authoritative copy.
        }
        optic.Materials.Register(new ConstantIndexMaterial("Independent medium", 2)); optic.Materials.Register("custom alias", new ConstantIndexMaterial("wrong", 3));
        doc.Replace(source.Id, source with { LocalCoordinateSystem = new(new(.8, 0, 0)) });
        var replay = await NonSequentialDesignArchiveStore.ReplayAsync(path);
        Assert.True(replay.MatchesRecorded); Assert.Equal(plan.SourceSceneSha256, replay.Archive.Manifest.SourceSceneSha256);
        Assert.Equal(.8, doc.Objects[0].LocalCoordinateSystem.Origin.X);
        if (tolerancing) Assert.Equal(tolerance!.Samples.Select(s => s.Values[0]), replay.Tolerance!.Samples.Select(s => s.Values[0]));
        else Assert.Equal(optimization!.BestValues, replay.Optimization!.BestValues);
    }

    [Fact]
    public async Task TamperedOrTruncatedArchivesAndCanceledSaveNeverReplaceValidOutput()
    {
        using var app = WorkbenchApplication.Create(); var source = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.SourceRay);
        var detector = app.NonSequential.AddObject(Dto.NonSequentialObjectKind.DetectorRectangle);
        var d = app.NonSequential.GetDocument().Objects.Single(o => o.Id == detector);
        app.NonSequential.UpdateObject(new(d.Id, d.Enabled, d.Visible, d.Kind, d.Name, null, null, 0, 0, 1, 0, 0, 0,
            new Dto.DetectorRectangleParameters(4, 4, 2, 2, true, true)));
        var variable = new NonSequentialDesignVariable(source, NonSequentialVariableDomain.Position, "X", -1, 1, .02);
        var target = new NonSequentialDesignTarget(detector, NonSequentialMetric.TotalPower, 1, .01);
        var plan = new NonSequentialDesignExperiment(1, app.NonSequential.GetDesignSceneIdentity(), [variable], [target], new());
        var result = await app.NonSequential.OptimizeAsync(plan.Variables, plan.Targets, plan.Settings);
        using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("valid.zip");
        await app.NonSequential.SaveDesignExperimentAsync(path, plan, result); var original = File.ReadAllBytes(path);
        using (var cts = new CancellationTokenSource())
        { cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.NonSequential.SaveDesignExperimentAsync(path, plan, result, cancellationToken: cts.Token)); }
        Assert.Equal(original, File.ReadAllBytes(path));
        var broken = files.Path("tampered.zip"); File.WriteAllBytes(broken, original);
        using (var stream = File.Open(broken, FileMode.Open)) using (var zip = new ZipArchive(stream, ZipArchiveMode.Update))
        { zip.GetEntry("plan.json")!.Delete(); await using var writer = zip.CreateEntry("plan.json").Open(); await writer.WriteAsync("{}"u8.ToArray()); }
        await Assert.ThrowsAsync<InvalidDataException>(() => NonSequentialDesignArchiveStore.InspectAsync(broken));
        var truncated = files.Path("truncated.zip"); File.WriteAllBytes(truncated, original[..(original.Length / 2)]);
        await Assert.ThrowsAsync<InvalidDataException>(() => NonSequentialDesignArchiveStore.InspectAsync(truncated));
        var identity = app.NonSequential.GetDesignSceneIdentity();
        app.NonSequential.AddObject(Dto.NonSequentialObjectKind.PlaneRectangle);
        var changed = app.NonSequential.GetDesignSceneIdentity(); Assert.NotEqual(identity, changed);
        await app.NonSequential.ReplayDesignExperimentAsync(path);
        Assert.Equal(changed, app.NonSequential.GetDesignSceneIdentity());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await app.NonSequential.SaveDesignExperimentAsync(path, plan, result));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task EmbeddedMeshStillBlocksLightAfterTheOriginalStlIsRemoved()
    {
        const string stl = """
            solid blocker
            facet normal 0 0 1
            outer loop
            vertex -2 -2 0.5
            vertex 2 -2 0.5
            vertex 0 2 0.5
            endloop
            endfacet
            endsolid blocker
            """;
        using var files = new CameraSensorFileTests.TemporaryFiles(); var stlPath = files.Path("blocker.stl");
        await File.WriteAllTextAsync(stlPath, stl);
        var mesh = NonSequentialStlImporter.Import(stlPath);
        var source = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay);
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with { LocalCoordinateSystem = new(new(0, 0, 1)) };
        var doc = new NonSequentialDocument("embedded mesh", [new("d", 550, 1, true)], [source, detector]);
        var asset = doc.AddMeshAsset(mesh);
        doc.Insert(1, new NonSequentialObjectDefinition(Guid.NewGuid(), "blocker", NonSequentialObjectKind.Mesh,
            true, true, new(new(0, 0, 0)), null, null, new MeshObjectParameters(asset)));
        var optic = Optic.CreateBlank(); var variable = new NonSequentialDesignVariable(source.Id, NonSequentialVariableDomain.Position, "X", -1, 1, .02);
        var goal = new NonSequentialDesignTarget(detector.Id, NonSequentialMetric.TotalPower, 0, .001);
        var plan = new NonSequentialDesignExperiment(1, NonSequentialSceneHasher.Compute(doc, optic.Materials), [variable], [goal], new());
        var result = NonSequentialDesignEngine.Optimize(doc, optic.Materials, plan.Variables, plan.Targets, plan.Settings);
        Assert.Equal(0, result.ValidationMetrics.Single().Value);
        var archive = files.Path("mesh.zip");
        await NonSequentialDesignArchiveStore.SaveAsync(archive, new([optic], 0, NonSequentialDocument: doc), optic.Materials, plan, NonSequentialDesignRunKind.Optimization, result);
        File.Delete(stlPath);
        var replay = await NonSequentialDesignArchiveStore.ReplayAsync(archive);
        Assert.True(replay.MatchesRecorded); Assert.Equal(0, replay.Optimization!.ValidationMetrics.Single().Value);
    }
}
