using System.Text.Json;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class SystemStateOperandTests
{
    public static TheoryData<string> Codes => new("PRIM", "CVIG", "IMSF");
    private static MeritOperandDefinition Row(string code, int a = 0, int b = 0) => new()
    { Type = code, ZemaxIntegerParameters = [a, b], ZemaxDataParameters = [0, 0, 0, 0] };
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Success(IEnumerable<MeritOperandEvaluation> rows) =>
        Assert.All(rows, row => Assert.True(string.IsNullOrEmpty(row.Error), row.Error));
    private static string Snapshot(Optic optic) => JsonSerializer.Serialize(optic.ToSnapshot(),
        new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PrimaryChangeOnlyAffectsFollowingRowsAndNeverSource(int wave)
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [Row("WLEN"), Row("PRIM", wave), Row("WLEN"), Row("EFFL")]);
        Success(rows);
        Assert.Equal(Value(optic, Row("WLEN")), rows[0].Value);
        Assert.Equal(optic.Wavelengths[wave - 1].Micrometers, rows[2].Value);
        var reference = Optic.FromSnapshot(optic.ToSnapshot());
        for (var i = 0; i < reference.Wavelengths.Count; i++) reference.Wavelengths[i].IsPrimary = i == wave - 1;
        Assert.Equal(reference.Paraxial.EstimateEffectiveFocalLength(), rows[3].Value, 10);
        Assert.Equal(before, Snapshot(optic));
        Assert.Equal(rows[0].Value, Value(optic, Row("WLEN")));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("goto")]
    [InlineData("end")]
    public void ControlFlowSkipsStateChanges(string mode)
    {
        var optic = Optic.CreateCookeTriplet(); var change = Row("PRIM", 3);
        var rows = mode == "disabled" ? new[] { change, Row("WLEN") }
            : new[] { Row(mode == "goto" ? "GOTO" : "ENDX", 3), change, Row("WLEN") };
        change.Enabled = mode != "disabled";
        var before = Snapshot(optic); var result = MeritFunctionCatalog.EvaluateAll(optic, rows); Success(result);
        Assert.Equal(mode == "end" ? 0 : Value(optic, Row("WLEN")), result[^1].Value);
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void InvalidPrimaryLeavesPreviousBatchStateAndOptimizerSeesFailure(int wave)
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var definitions = new[] { Row("PRIM", 2), Row("PRIM", wave), Row("WLEN") };
        var rows = MeritFunctionCatalog.EvaluateAll(optic, definitions);
        Assert.NotEmpty(rows[1].Error); Assert.True(double.IsPositiveInfinity(rows[1].Contribution));
        Assert.Equal(optic.Wavelengths[1].Micrometers, rows[2].Value);
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, definitions));
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData(0, .6, 0)]
    [InlineData(1, 0, .6)]
    [InlineData(2, .4, .5)]
    public void ClearVignettingChangesFollowingRayAndPreservesAllSourceFields(int field, double px, double py)
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var item in optic.Fields) { item.VignetteFactorX = .25; item.VignetteFactorY = .4; }
        var ray = Row(px == 0 ? "REAY" : "REAX", 1, 1);
        var normalized = FieldCoordinates.Normalize(optic.Fields, optic.Fields[field].X, optic.Fields[field].Y);
        ray.Hx = normalized.X; ray.Hy = normalized.Y; ray.Px = px; ray.Py = py;
        ray.ZemaxDataParameters = [ray.Hx, ray.Hy, px, py];
        var before = Snapshot(optic); var rows = MeritFunctionCatalog.EvaluateAll(optic, [ray, Row("CVIG"), ray]); Success(rows);
        var reference = Optic.FromSnapshot(optic.ToSnapshot());
        foreach (var item in reference.Fields) { item.VignetteFactorX = 0; item.VignetteFactorY = 0; }
        Assert.Equal(Value(reference, ray), rows[2].Value, 11);
        Assert.True(Math.Abs(rows[0].Value - rows[2].Value) > 1e-5);
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ImageSelectionKeepsSurfaceAndRestoreKeepsOtherTemporaryState(int surface)
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var selected = IntermediateImageSystem.Create(optic, surface, false);
        Assert.Equal(surface + 1, selected.SurfaceGroup.Items.Count);
        for (var i = 0; i <= surface; i++)
        {
            Assert.Equal(optic.SurfaceGroup.Items[i].CoordinateSystem, selected.SurfaceGroup.Items[i].CoordinateSystem);
            Assert.Equal(optic.SurfaceGroup.Items[i].Radius, selected.SurfaceGroup.Items[i].Radius);
            Assert.Equal(optic.SurfaceGroup.Items[i].MaterialAfter.Name, selected.SurfaceGroup.Items[i].MaterialAfter.Name);
            Assert.NotSame(optic.SurfaceGroup.Items[i], selected.SurfaceGroup.Items[i]);
        }
        var rows = MeritFunctionCatalog.EvaluateAll(optic,
            [Row("PRIM", 3), Row("CVIG"), Row("IMSF", surface), Row("TOTR"), Row("IMSF"), Row("TOTR"), Row("WLEN")]);
        Success(rows); Assert.Equal(Value(selected, Row("TOTR")), rows[3].Value);
        Assert.Equal(Value(optic, Row("TOTR")), rows[5].Value);
        Assert.Equal(optic.Wavelengths[2].Micrometers, rows[6].Value);
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void SelectedTiltedSurfaceKeepsExplicitCoordinates()
    {
        var optic = Optic.CreateCookeTriplet(); var surface = optic.SurfaceGroup.Items[5];
        surface.CoordinateSystem = new CoordinateSystem(new Vector3D(1, -2, 31), 3, 4, 5);
        var result = IntermediateImageSystem.Create(optic, 5, false);
        Assert.Equal(surface.CoordinateSystem, result.SurfaceGroup.Items[^1].CoordinateSystem);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RefocusUsesSelectedPrimaryAndFormalParaxialMarginalRay(int wave)
    {
        var optic = Optic.CreateCookeTriplet();
        for (var i = 0; i < optic.Wavelengths.Count; i++) optic.Wavelengths[i].IsPrimary = i == wave - 1;
        var before = Snapshot(optic); var focused = IntermediateImageSystem.Create(optic, 6, true);
        Assert.Equal(8, focused.SurfaceGroup.Items.Count);
        var ray = optic.Paraxial.TraceNormalizedRay(6, 0, 0, 0, 1, optic.Wavelengths[wave - 1].Micrometers);
        Assert.Equal(-ray.Position.Y * ray.Direction.Z / ray.Direction.Y, focused.SurfaceGroup.Items[6].Thickness, 10);
        var atImage = focused.Paraxial.TraceNormalizedRay(7, 0, 0, 0, 1, optic.Wavelengths[wave - 1].Micrometers);
        Assert.InRange(Math.Abs(atImage.Position.Y), 0, 1e-10);
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [Row("IMSF", 6, 1), Row("TOTR")]); Success(rows);
        Assert.Equal(Value(focused, Row("TOTR")), rows[1].Value, 10);
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(999, 0)]
    [InlineData(1, 0)]
    [InlineData(6, -1)]
    [InlineData(6, 2)]
    public void InvalidImageSelectionRollsBackToPreviousImage(int surface, int refocus)
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [Row("IMSF", 5), Row("TOTR"), Row("IMSF", surface, refocus), Row("TOTR")]);
        Assert.NotEmpty(rows[2].Error); Assert.Equal(rows[1].Value, rows[3].Value); Assert.Empty(rows[3].Error);
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData("imageHeight")]
    [InlineData("afocal")]
    [InlineData("parallel")]
    [InlineData("tilted")]
    public void UnimplementedImageModesFailExplicitly(string mode)
    {
        var optic = Optic.CreateCookeTriplet();
        if (mode == "imageHeight") optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        if (mode == "afocal") optic.ImageSpaceAfocal = true;
        if (mode == "parallel") foreach (var surface in optic.SurfaceGroup.Items) surface.Radius = 0;
        if (mode == "tilted") optic.SurfaceGroup.Items[5].CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10), 1, 0, 0);
        var before = Snapshot(optic); var rows = MeritFunctionCatalog.EvaluateAll(optic, [Row("IMSF", 6, 1)]);
        Assert.NotEmpty(rows[0].Error); Assert.True(double.IsNaN(rows[0].Value)); Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void StateRowsIgnoreTargetsAndWeightsButCannotSupplyNumericReferences(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var state = Row(code, code == "PRIM" ? 2 : 0);
        state.Target = 123; state.Weight = 25;
        var definitions = new[] { state, Row("WLEN") };
        var evaluations = MeritFunctionCatalog.EvaluateAll(optic, definitions); Success(evaluations);
        Assert.Equal(0, evaluations[0].Contribution);
        var operands = MeritFunctionCatalog.CreateOperands(optic, definitions);
        Assert.Equal(0, operands[0].Target); Assert.Equal(0, operands[0].Weight); Assert.Equal(0, operands[0].Residual());
        Assert.Equal(evaluations.Select(v => v.Value), MeritFunctionCatalog.EvaluateOptimizationValues(optic, definitions));
        var references = MeritFunctionCatalog.EvaluateAll(optic, [state, Row("OPVA", 1)]);
        Assert.NotEmpty(references[1].Error);
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, state).Error);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CancellationAndNestedEvaluationLeaveSourceAndOuterBatchUnchanged(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic); var row = Row(code, code == "PRIM" ? 2 : 0);
        using var outer = MeritFunctionCatalog.BeginEvaluationBatch();
        var initial = Value(optic, Row("EFFL"));
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel(); using var scope = ComputationCancellation.Push(cancellation.Token);
            Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.EvaluateAll(optic, [row]));
        }
        Success(MeritFunctionCatalog.EvaluateAll(optic, [row, Row("EFFL")]));
        Assert.Equal(initial, Value(optic, Row("EFFL"))); Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public async Task ConcurrentBatchesDoNotShareTemporaryPrimary()
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var tasks = Enumerable.Range(0, 18).Select(i => Task.Run(() =>
        {
            var wave = i % 3 + 1;
            var rows = MeritFunctionCatalog.EvaluateAll(optic, [Row("PRIM", wave), Row("WLEN")]); Success(rows);
            Assert.Equal(optic.Wavelengths[wave - 1].Micrometers, rows[1].Value);
        }));
        await Task.WhenAll(tasks); Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task NativeProjectAndEditorPreserveLocalControlParameters(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var original = Row(code, code == "PRIM" ? 2 : code == "IMSF" ? 6 : 0);
        optic.MeritFunctionOperands.Add(original);
        var path = Path.Combine(Path.GetTempPath(), $"system-state-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var row = Assert.Single(restored.MeritFunctionOperands);
            Assert.False(row.CompatibilityOnly); Assert.Equal(original.ZemaxIntegerParameters, row.ZemaxIntegerParameters);
            Success(MeritFunctionCatalog.EvaluateAll(restored, [row]));
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "state",
            ZemaxInt1: original.ZemaxIntegerParameters[0], ZemaxInt2: original.ZemaxIntegerParameters[1])]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(original.ZemaxIntegerParameters[0], dto.ZemaxInt1);
        Assert.False(dto.CompatibilityOnly);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        var editor = new MeritOperandEditorRow(dto, type);
        if (code == "PRIM") editor.Parameter1 = 3;
        if (code == "IMSF") editor.Parameter2 = 1;
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(code == "PRIM" ? 3 : original.ZemaxIntegerParameters[0], saved.ZemaxInt1);
        Assert.Equal(code == "IMSF" ? 1 : 0, saved.ZemaxInt2);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void UnverifiedNativeMappingsRemainReadOnlyAcrossSnapshots(string code)
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var optic = OpticalFormatCatalog.Import(text + $"\n{code} 2 0 0 0 0 0 1.5 2.5 0 0\n", ".zmx");
        var row = optic.MeritFunctionOperands.Last();
        Assert.Equal(code != "CVIG", row.CompatibilityOnly); Assert.Equal(code == "CVIG", row.Enabled);
        var restored = Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands.Last();
        Assert.Equal(row.CompatibilityOnly, restored.CompatibilityOnly); Assert.Equal(row.Enabled, restored.Enabled);
        Assert.Equal(row.ZemaxIntegerParameters, restored.ZemaxIntegerParameters);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void IsolatedChangesDoNotPolluteOriginalTraceCacheAndNextBatchReadsEdits(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var cache = new RayTraceCache(32, 512);
        optic.ConfigureRayTraceCache(cache, 71);
        var ray = Row("REAY", 1, 1); ray.Py = .5; ray.ZemaxDataParameters = [0, 0, 0, .5];
        var initial = Value(optic, ray); Value(optic, ray);
        var hits = cache.Statistics.Hits;
        var state = Row(code, code == "PRIM" ? 3 : code == "IMSF" ? 6 : 0);
        Success(MeritFunctionCatalog.EvaluateAll(optic, [state, ray]));
        Assert.Equal(initial, Value(optic, ray)); Assert.True(cache.Statistics.Hits > hits);
        optic.Aperture.Value *= .8;
        var updated = Value(optic, ray); Assert.NotEqual(initial, updated);
        var next = MeritFunctionCatalog.EvaluateAll(optic, [state, ray]); Success(next);
        Assert.Equal(updated, next[1].Value, 10);
    }

    [Fact]
    public void TemporaryCopyPreservesRuntimeMaterialDefinition()
    {
        var optic = Optic.CreateCookeTriplet(); var surface = optic.SurfaceGroup.Items[3];
        surface.MaterialAfter = new CatalogGlassMaterial("RUNTIME:Custom", "RUNTIME", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.55, 1.65]);
        var row = Row("INDX", 3, 1); var expected = Value(optic, row); var before = Snapshot(optic);
        var results = MeritFunctionCatalog.EvaluateAll(optic, [Row("PRIM", 3), row, Row("CVIG"), row]);
        Success(results); Assert.Equal(expected, results[1].Value); Assert.Equal(expected, results[3].Value);
        Assert.Equal(before, Snapshot(optic));
    }
}
