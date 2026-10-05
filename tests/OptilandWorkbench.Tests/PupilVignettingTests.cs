using System.Text.Json;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class PupilVignettingTests
{
    private static Optic PlaneSystem()
    {
        var optic = new Optic("Vignetting transform");
        optic.Aperture.Value = 10;
        optic.Fields.Add(new FieldPoint { Label = "axis" });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces(
        [
            new() { Thickness = double.PositiveInfinity, SemiDiameter = 50 },
            new() { Thickness = 10, SemiDiameter = 5, IsStop = true },
            new() { Thickness = 0, SemiDiameter = 50 }
        ]);
        return optic;
    }

    [Theory]
    [InlineData(0, .3, -.325)]
    [InlineData(90, .325, .3)]
    [InlineData(180, -.3, .325)]
    [InlineData(-90, -.325, -.3)]
    public void CompressionAndShiftPrecedeRotation(double angle, double x, double y)
    {
        var transformed = new PupilVignetting(.1, -.1, .5, .25, angle).Transform(.4, -.3);
        Assert.Equal(x, transformed.X, 13); Assert.Equal(y, transformed.Y, 13);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 90)]
    [InlineData(true, 0)]
    [InlineData(true, 90)]
    public void GenericAndBatchLaunchApplyExactlyOneTransform(bool aiming, double angle)
    {
        var optic = PlaneSystem(); var field = optic.Fields[0];
        field.VignetteFactorX = .5; field.VignetteFactorY = .25;
        field.VignetteDecenterX = .1; field.VignetteDecenterY = -.1; field.VignetteAngleDegrees = angle;
        var generator = optic.SequentialRayTracer.RayGenerator;
        var single = generator.GenerateGeneric(0, 0, .4, -.3, .55, aiming);
        var batch = generator.GenerateNormalizedPupilSamples(0, 0, .55, [new(.4, -.3, 1)], aiming);
        using var first = optic.SequentialRayTracer.Trace(single, TraceRequest.FullHistory());
        using var second = optic.SequentialRayTracer.Trace(batch, TraceRequest.FullHistory());
        Assert.True(first.TryGetSample(0, 1, out var a)); Assert.True(second.TryGetSample(0, 1, out var b));
        Assert.Equal(angle == 0 ? 1.5 : 1.625, a.Position.X, 10);
        Assert.Equal(angle == 0 ? -1.625 : 1.5, a.Position.Y, 10);
        Assert.Equal(a.Position, b.Position); Assert.Equal(a.Direction, b.Direction);
        var unmodified = generator.GenerateNormalizedPupilSamples(0, 0, .55, [new(.4, -.3, 1)], aiming,
            applyVignettingFactors: false);
        using var unmodifiedTrace = optic.SequentialRayTracer.Trace(unmodified, TraceRequest.FullHistory());
        Assert.True(unmodifiedTrace.TryGetSample(0, 1, out var u));
        Assert.Equal(2, u.Position.X, 10); Assert.Equal(-1.5, u.Position.Y, 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenerateForAndNormalizedBundlesShareTheSameDecenteredPupil(bool normalized)
    {
        var optic = PlaneSystem(); var field = optic.Fields[0];
        field.VignetteDecenterX = .2; field.VignetteDecenterY = -.1; field.VignetteAngleDegrees = 90;
        var generator = optic.SequentialRayTracer.RayGenerator;
        generator.Settings.SamplesPerField = 1;
        var bundle = normalized ? generator.GenerateNormalized(0, 0, .55, 1, "hexapolar") : generator.GenerateFor(field);
        Assert.Single(bundle.Rays);
        using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FullHistory());
        Assert.True(trace.TryGetSample(0, 1, out var sample));
        Assert.Equal(.5, sample.Position.X, 10); Assert.Equal(1, sample.Position.Y, 10);
    }

    [Fact]
    public void ClearVignettingResetsAllFiveFactorsOnAnIsolatedCopy()
    {
        var optic = PlaneSystem(); var field = optic.Fields[0];
        field.VignetteDecenterX = .2; field.VignetteDecenterY = -.1; field.VignetteAngleDegrees = 90;
        var row = new MeritOperandDefinition { Type = "REAX", ZemaxIntegerParameters = [1, 1], ZemaxDataParameters = [0, 0, 0, 0] };
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [row, new() { Type = "CVIG" }, row]);
        Assert.All(rows, value => Assert.True(string.IsNullOrEmpty(value.Error), value.Error));
        Assert.Equal(.5, rows[0].Value, 10); Assert.Equal(0, rows[2].Value, 10);
        Assert.Equal(.2, field.VignetteDecenterX); Assert.Equal(90, field.VignetteAngleDegrees);
        var analysisCopy = AnalysisTrace.PrepareVignettingFactors(optic, true);
        Assert.NotSame(optic, analysisCopy);
        Assert.Equal(PupilVignetting.Identity, PupilVignetting.FromField(analysisCopy.Fields[0]));
        Assert.NotEqual(PupilVignetting.Identity, PupilVignetting.FromField(field));
    }

    [Fact]
    public async Task FiveFactorsRoundTripZmxProjectDtoAndUnrelatedFieldEdits()
    {
        var optic = PlaneSystem(); var field = optic.Fields[0];
        field.VignetteFactorX = .25; field.VignetteFactorY = -.1;
        field.VignetteDecenterX = -.2; field.VignetteDecenterY = .4; field.VignetteAngleDegrees = -37;
        var expected = PupilVignetting.FromField(field);
        Assert.Equal(expected, PupilVignetting.FromField(field.Clone()));
        var text = OpticalFormatCatalog.Export(optic, ".zmx");
        Assert.Contains("VDXN -0.2", text); Assert.Contains("VDYN 0.4", text); Assert.Contains("VANN -37", text);
        var imported = OpticalFormatCatalog.Import(text, ".zmx");
        Assert.Equal(expected, PupilVignetting.FromField(imported.Fields[0]));
        Assert.Equal(6, imported.ToSnapshot().SchemaVersion);
        var path = Path.Combine(Path.GetTempPath(), $"vignetting-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            Assert.Equal(expected, PupilVignetting.FromField(restored.Fields[0]));
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        var row = app.Prescription.GetFields()[0] with { VignetteDecenterX = -.2, VignetteDecenterY = .4, VignetteAngleDegrees = -37 };
        app.Prescription.UpdateField(row);
        var editor = new FieldEditorRow(app.Prescription.GetFields()[0]); editor.Label = "edited";
        app.Prescription.UpdateField(editor.ToDto());
        var saved = app.Prescription.GetFields()[0];
        Assert.Equal(-.2, saved.VignetteDecenterX); Assert.Equal(.4, saved.VignetteDecenterY); Assert.Equal(-37, saved.VignetteAngleDegrees);
    }

    [Fact]
    public void OldSnapshotWithoutNewFieldsKeepsIdentityTransform()
    {
        var snapshot = PlaneSystem().ToSnapshot() with { SchemaVersion = 5 };
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\"Vignette(?:DecenterX|DecenterY|AngleDegrees)\":0", "");
        var restored = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(json, new JsonSerializerOptions
        { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals })!);
        Assert.Equal(PupilVignetting.Identity, PupilVignetting.FromField(restored.Fields[0]));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("angle")]
    public void NonFiniteNewFactorsFailModelAndSnapshotValidation(string component)
    {
        var optic = PlaneSystem(); var field = optic.Fields[0];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (component == "x") field.VignetteDecenterX = double.NaN;
            if (component == "y") field.VignetteDecenterY = double.PositiveInfinity;
            if (component == "angle") field.VignetteAngleDegrees = double.NegativeInfinity;
        });
        var snapshot = optic.ToSnapshot();
        var invalid = snapshot.Fields[0] with
        {
            VignetteDecenterX = component == "x" ? double.NaN : 0,
            VignetteDecenterY = component == "y" ? double.PositiveInfinity : 0,
            VignetteAngleDegrees = component == "angle" ? double.NegativeInfinity : 0
        };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot with { Fields = [invalid] }));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("angle")]
    public void NewFactorsDetachMutatedOpticFromSharedTraceCache(string component)
    {
        var optic = PlaneSystem();
        var cache = new RayTraceCache(32, 512);
        optic.ConfigureRayTraceCache(cache, 123);
        var generator = optic.SequentialRayTracer.RayGenerator;
        var rays = generator.GenerateGeneric(0, 0, .4, -.3, .55);
        using (optic.SequentialRayTracer.Trace(rays, TraceRequest.FullHistory())) { }
        using (optic.SequentialRayTracer.Trace(rays, TraceRequest.FullHistory())) { }
        var hits = cache.Statistics.Hits;
        Assert.True(hits > 0);
        if (component == "x") optic.Fields[0].VignetteDecenterX = .1;
        if (component == "y") optic.Fields[0].VignetteDecenterY = .1;
        if (component == "angle") optic.Fields[0].VignetteAngleDegrees = 90;
        // Even an identical input bundle must no longer use the source revision's cache.
        using (optic.SequentialRayTracer.Trace(rays, TraceRequest.FullHistory())) { }
        Assert.Equal(hits, cache.Statistics.Hits);
        var changed = generator.GenerateGeneric(0, 0, .4, -.3, .55);
        using var trace = optic.SequentialRayTracer.Trace(changed, TraceRequest.FullHistory());
        Assert.True(trace.TryGetSample(0, 1, out var sample));
        Assert.Equal(component == "x" ? 2.5 : component == "angle" ? 1.5 : 2, sample.Position.X, 10);
        Assert.Equal(component == "y" ? -1 : component == "angle" ? 2 : -1.5, sample.Position.Y, 10);
    }

    [Fact]
    public void RemovingFactorsPreservesRuntimeMaterialAndSourceData()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[3].MaterialAfter = new CatalogGlassMaterial("RUNTIME:Custom", "RUNTIME", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.55, 1.65]);
        optic.Fields[1].VignetteDecenterX = .1;
        var row = new MeritOperandDefinition { Type = "INDX", ZemaxIntegerParameters = [3, 1] };
        var expected = MeritFunctionCatalog.Evaluate(optic, row);
        var copy = AnalysisTrace.PrepareVignettingFactors(optic, true);
        var actual = MeritFunctionCatalog.Evaluate(copy, row);
        Assert.True(string.IsNullOrEmpty(actual.Error), actual.Error);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(.1, optic.Fields[1].VignetteDecenterX);
        Assert.Equal(PupilVignetting.Identity, PupilVignetting.FromField(copy.Fields[1]));
    }
}
