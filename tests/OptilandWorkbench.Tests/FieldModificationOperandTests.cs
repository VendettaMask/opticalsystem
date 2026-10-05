using System.Text.Json;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class FieldModificationOperandTests
{
    private static MeritOperandDefinition Modify(int field, double hx = .25, double hy = .5,
        double dx = .1, double dy = -.15, double cx = .2, double cy = .3) => new()
        { Type = "FDMO", ZemaxIntegerParameters = [field, 0], ZemaxDataParameters = [hx, hy, dx, dy, cx, cy] };
    private static MeritOperandDefinition Restore(int field) => new() { Type = "FDRE", ZemaxIntegerParameters = [field, 0] };
    private static MeritOperandDefinition Centroid(int field) => new()
    { Type = "CENX", ZemaxIntegerParameters = [0, 1], ZemaxDataParameters = [field, 0, 5, 0] };
    private static string Snapshot(Optic optic) => JsonSerializer.Serialize(optic.ToSnapshot(),
        new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
    private static void Success(IEnumerable<MeritOperandEvaluation> rows) =>
        Assert.All(rows, row => Assert.True(string.IsNullOrEmpty(row.Error), row.Error));
    private static double Value(Optic optic, MeritOperandDefinition row)
    { var result = MeritFunctionCatalog.Evaluate(optic, row); Success([result]); return result.Value; }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ModifiedFieldAffectsFollowingMeasurementsAndRestoresExactly(int field)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields[field - 1].VignetteAngleDegrees = 23;
        var before = Snapshot(optic); var original = Value(optic, Centroid(field));
        var rows = MeritFunctionCatalog.EvaluateAll(optic,
            [Centroid(field), Modify(field), Centroid(field), Restore(field), Centroid(field)]);
        Success(rows);
        var reference = Optic.FromSnapshot(optic.ToSnapshot());
        var target = reference.Fields[field - 1];
        target.X = 5; target.Y = 10; target.VignetteDecenterX = .1; target.VignetteDecenterY = -.15;
        target.VignetteFactorX = .2; target.VignetteFactorY = .3;
        Assert.Equal(Value(reference, Centroid(field)), rows[2].Value, 10);
        Assert.True(Math.Abs(original - rows[2].Value) > 1e-4);
        Assert.Equal(original, rows[0].Value, 11); Assert.Equal(original, rows[4].Value, 11);
        Assert.Equal(0, rows[1].Contribution); Assert.Equal(0, rows[3].Contribution);
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RepeatedModificationRestoresFirstSavedDataAndKeepsOtherOverrides(int restoreField)
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var rows = MeritFunctionCatalog.EvaluateAll(optic,
            [Modify(1), Modify(2, -.2, .4), Modify(1, -.4, -.2), Restore(restoreField), Centroid(1), Centroid(2)]);
        Success(rows);
        var expected = Optic.FromSnapshot(optic.ToSnapshot());
        var changed = expected.Fields[restoreField == 1 ? 1 : 0];
        changed.X = restoreField == 1 ? -4 : -8; changed.Y = restoreField == 1 ? 8 : -4;
        changed.VignetteDecenterX = .1; changed.VignetteDecenterY = -.15;
        changed.VignetteFactorX = .2; changed.VignetteFactorY = .3;
        Assert.Equal(Value(expected, Centroid(1)), rows[4].Value, 10);
        Assert.Equal(Value(expected, Centroid(2)), rows[5].Value, 10);
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void FieldRestoreKeepsPrimaryAndImageStateAndUnmodifiedRestoreIsANoop()
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var rows = MeritFunctionCatalog.EvaluateAll(optic,
        [
            new() { Type = "PRIM", ZemaxIntegerParameters = [3, 0] },
            new() { Type = "IMSF", ZemaxIntegerParameters = [6, 0] },
            new() { Type = "TOTR" }, Modify(2), Restore(2), Restore(1),
            new() { Type = "TOTR" }, new() { Type = "WLEN" }
        ]);
        Success(rows); Assert.Equal(rows[2].Value, rows[6].Value);
        Assert.Equal(optic.Wavelengths[2].Micrometers, rows[7].Value);
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void ConfigurationBoundaryRestoresFieldsWhenSelectingSameConfiguration()
    {
        var optic = Optic.CreateCookeTriplet(); var before = Value(optic, Centroid(2));
        var rows = MeritFunctionCatalog.EvaluateAll(optic,
            [Modify(2), Centroid(2), new() { Type = "CONF", ZemaxIntegerParameters = [1, 0] }, Centroid(2)]);
        Assert.NotEqual(before, rows[1].Value); Assert.Empty(rows[2].Error);
        Assert.Equal(before, rows[3].Value, 10);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("goto")]
    [InlineData("end")]
    public void SkippedFieldChangesHaveNoEffect(string mode)
    {
        var optic = Optic.CreateCookeTriplet(); var change = Modify(2); change.Enabled = mode != "disabled";
        MeritOperandDefinition[] definitions = mode == "disabled" ? [change, Centroid(2)]
            : [new() { Type = mode == "goto" ? "GOTO" : "ENDX", ZemaxIntegerParameters = [3, 0] }, change, Centroid(2)];
        var before = Snapshot(optic); var rows = MeritFunctionCatalog.EvaluateAll(optic, definitions); Success(rows);
        Assert.Equal(mode == "end" ? 0 : Value(optic, Centroid(2)), rows[^1].Value);
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData("FDMO", 0)]
    [InlineData("FDMO", 4)]
    [InlineData("FDRE", 0)]
    [InlineData("FDRE", -1)]
    public void InvalidFieldDoesNotReplacePriorBatchState(string code, int field)
    {
        var optic = Optic.CreateCookeTriplet(); var invalid = code == "FDMO" ? Modify(field) : Restore(field);
        var definitions = new[] { Modify(2), Centroid(2), invalid, Centroid(2) };
        var rows = MeritFunctionCatalog.EvaluateAll(optic, definitions);
        Assert.NotEmpty(rows[2].Error); Assert.Equal(rows[1].Value, rows[3].Value);
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, definitions));
    }

    [Theory]
    [InlineData(0, 1.1)]
    [InlineData(1, -1.1)]
    [InlineData(2, double.NaN)]
    [InlineData(3, double.PositiveInfinity)]
    [InlineData(4, double.NegativeInfinity)]
    [InlineData(5, double.NaN)]
    public void InvalidParameterRollsBackAllFields(int slot, double value)
    {
        var optic = Optic.CreateCookeTriplet(); var invalid = Modify(2); invalid.ZemaxDataParameters[slot] = value;
        var before = Snapshot(optic);
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [Modify(2), Centroid(2), invalid, Centroid(2), Restore(2), Centroid(2)]);
        Assert.NotEmpty(rows[2].Error); Assert.Equal(rows[1].Value, rows[3].Value);
        Assert.Equal(Value(optic, Centroid(2)), rows[5].Value); Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData("FDMO")]
    [InlineData("FDRE")]
    public async Task LocalEightSlotsSurviveProjectAndEditorButNativeRowsRemainReadOnly(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var original = code == "FDMO" ? Modify(2) : Restore(2);
        optic.MeritFunctionOperands.Add(original);
        var path = Path.Combine(Path.GetTempPath(), $"field-modification-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var row = Assert.Single(restored.MeritFunctionOperands);
            Assert.False(row.CompatibilityOnly); Assert.Equal(original.ZemaxDataParameters, row.ZemaxDataParameters);
            Success(MeritFunctionCatalog.EvaluateAll(restored, [row]));
        }
        finally { File.Delete(path); }
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 2, 0, .25, .5, 0, 0, 0, 0, 0, 0, "field",
            ZemaxInt1: 2, ZemaxInt2: 0, ZemaxData1: .25, ZemaxData2: .5, ZemaxData3: .1, ZemaxData4: -.15,
            ZemaxData5: code == "FDMO" ? .2 : null, ZemaxData6: code == "FDMO" ? .3 : null)]);
        var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        var editor = new MeritOperandEditorRow(Assert.Single(app.Optimization.GetMeritFunction()), type);
        if (code == "FDMO") { editor.Parameter7 = .35; editor.Parameter8 = -.25; }
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.False(saved.CompatibilityOnly);
        if (code == "FDMO") { Assert.Equal(.35, saved.ZemaxData5); Assert.Equal(-.25, saved.ZemaxData6); }
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var imported = OpticalFormatCatalog.Import(text + $"\n{code} 2 0 .25 .5 .1 -.15 0 1 0 0 .2 .3\n", ".zmx");
        var native = imported.MeritFunctionOperands.Last();
        Assert.True(native.CompatibilityOnly); Assert.False(native.Enabled);
        var copied = Optic.FromSnapshot(imported.ToSnapshot()).MeritFunctionOperands.Last();
        Assert.Equal(native.Comment, copied.Comment); Assert.True(copied.CompatibilityOnly);
    }

    [Theory]
    [InlineData("FDMO")]
    [InlineData("FDRE")]
    public void IsolatedStateRequiresOrderedContextAndCancellationNeverModifiesSource(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var row = code == "FDMO" ? Modify(2) : Restore(2);
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, row).Error);
        using (ComputationCancellation.Push(new CancellationToken(true)))
            Assert.Throws<OperationCanceledException>(() => MeritFunctionCatalog.EvaluateAll(optic, [row]));
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public async Task ConcurrentFieldOverridesDoNotLeakBetweenBatches()
    {
        var optic = Optic.CreateCookeTriplet(); var before = Snapshot(optic);
        var references = Enumerable.Range(0, 6).Select(i => MeritFunctionCatalog.EvaluateAll(optic,
            [Modify(2, i * .05, .4), Centroid(2)])[1].Value).ToArray();
        await Task.WhenAll(Enumerable.Range(0, 18).Select(i => Task.Run(() =>
        {
            var rows = MeritFunctionCatalog.EvaluateAll(optic, [Modify(2, i % 6 * .05, .4), Centroid(2)]);
            Success(rows); Assert.Equal(references[i % 6], rows[1].Value);
        })));
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void RequirementsMarkerKeepsRowsButAddsNeitherResidualNorUsableMathValue()
    {
        var optic = Optic.CreateCookeTriplet();
        var marker = new MeritOperandDefinition { Type = "REQS", Target = 12, Weight = 99, Comment = "requirement start" };
        var rows = MeritFunctionCatalog.EvaluateAll(optic,
            [marker, new() { Type = "CONS", Hx = 2 }, new() { Type = "SUMM", ZemaxIntegerParameters = [1, 2] }]);
        Assert.Equal(0, rows[0].Contribution); Assert.NotEmpty(rows[2].Error);
        Assert.Empty(MeritFunctionCatalog.CreateOperands(optic, [marker]));
        Assert.Empty(MeritFunctionCatalog.EvaluateOptimizationValues(optic, [marker]));
        Assert.Equal(0, MeritFunctionCatalog.CreateOperand(optic, marker).Weight);
        optic.MeritFunctionOperands.Add(marker);
        var restored = Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands.Single();
        Assert.Equal(marker.Comment, restored.Comment); Assert.False(restored.CompatibilityOnly);
    }

    [Fact]
    public void TemporaryFieldsPreserveSourceCacheAndFollowingBatchesReadSourceEdits()
    {
        var optic = Optic.CreateCookeTriplet(); var cache = new RayTraceCache(32, 4096);
        optic.ConfigureRayTraceCache(cache, 73);
        var original = Value(optic, Centroid(2)); Value(optic, Centroid(2));
        var hits = cache.Statistics.Hits;
        var modified = MeritFunctionCatalog.EvaluateAll(optic, [Modify(2), Centroid(2), Restore(2), Centroid(2)]);
        Success(modified); Assert.NotEqual(original, modified[1].Value);
        Assert.Equal(original, modified[3].Value); Assert.Equal(original, Value(optic, Centroid(2)));
        Assert.True(cache.Statistics.Hits > hits);
        optic.Fields[1].X = 3;
        var changed = Value(optic, Centroid(2)); Assert.NotEqual(original, changed);
        var next = MeritFunctionCatalog.EvaluateAll(optic, [Modify(2), Restore(2), Centroid(2)]);
        Success(next); Assert.Equal(changed, next[2].Value);
    }
}
