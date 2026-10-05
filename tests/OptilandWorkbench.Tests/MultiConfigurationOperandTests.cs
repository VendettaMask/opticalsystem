using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class MultiConfigurationOperandTests
{
    [Fact]
    public void OrderedConfigurationSwitchUsesOneBasedNumbersWithoutChangingInputs()
    {
        var context = Context(); var before = Snapshot(context);
        var definitions = new[] { Thickness(), Row("CONF", 1), Thickness(), Row("CONF", 2), Thickness(), Row("SUMM", 3, 5) };
        definitions[1].Target = 99; definitions[1].Weight = 999;
        var values = MeritFunctionCatalog.EvaluateAll(context, definitions); Success(values);
        Assert.Equal(new double[] { 9, 0, 5, 0, 9, 14 }, values.Select(v => v.Value));
        Assert.Equal(0, values[1].Contribution);
        Assert.Equal(before, Snapshot(context)); Assert.Equal(1, context.ActiveConfigurationIndex);
        Assert.Equal(0, MeritFunctionCatalog.CreateOperands(context, definitions)[1].Weight);
        Assert.Equal(values.Select(v => v.Value), MeritFunctionCatalog.EvaluateOptimizationValues(context, definitions));
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(context.ActiveOptic, Row("CONF", 1)).Error);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void InvalidConfigurationDoesNotSwitchOrHideOptimizationFailure(int number)
    {
        var context = Context(); var definitions = new[] { Row("CONF", 1), Row("CONF", number), Thickness() };
        var rows = MeritFunctionCatalog.EvaluateAll(context, definitions);
        Assert.NotEmpty(rows[1].Error); Assert.True(double.IsNaN(rows[1].Value)); Assert.Equal(5, rows[2].Value);
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(context, definitions));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("goto")]
    [InlineData("end")]
    public void SkippedConfigurationCannotChangeLaterRows(string mode)
    {
        var row = Row("CONF", 1); row.Enabled = mode != "disabled";
        var prefix = mode == "goto" ? Row("GOTO", 3) : mode == "end" ? Row("ENDX") : Row("BLNK");
        var rows = MeritFunctionCatalog.EvaluateAll(Context(), [prefix, row, Thickness()]); Success(rows);
        Assert.Equal(mode == "end" ? 0 : 9, rows[2].Value);
    }

    [Theory]
    [InlineData("PRIM", 1)]
    [InlineData("CVIG", 0)]
    [InlineData("IMSF", 0)]
    public void UnverifiedStateCombinationsFailWithoutSwitching(string code, int argument)
    {
        var rows = MeritFunctionCatalog.EvaluateAll(Context(), [Row(code, argument), Row("CONF", 1), Thickness()]);
        Assert.Empty(rows[0].Error); Assert.Contains("尚未核实", rows[1].Error); Assert.Equal(9, rows[2].Value);
    }

    [Fact]
    public void FieldScaleFollowsSelectedConfigurationAndSameConfigurationRestoresFields()
    {
        var context = Context(); context.Configurations[0].Fields[2].Y = 40;
        var before = Snapshot(context);
        var modify = Row("FDMO", 2); modify.ZemaxDataParameters = [0, .5, 0, 0, 0, 0];
        var ray = Row("REAY", 7, 1); ray.Field = 2;
        var expected = Optic.FromSnapshot(context.Configurations[0].ToSnapshot()); expected.Fields[1].Y = 20;
        var rows = MeritFunctionCatalog.EvaluateAll(context, [Row("CONF", 1), modify, ray, Row("CONF", 1), ray, Row("CONF", 2), ray]);
        Success(rows);
        Near(MeritFunctionCatalog.Evaluate(expected, ray).Value, rows[2].Value);
        Near(MeritFunctionCatalog.Evaluate(context.Configurations[0], ray).Value, rows[4].Value);
        Near(MeritFunctionCatalog.Evaluate(context.Configurations[1], ray).Value, rows[6].Value);
        Assert.Equal(before, Snapshot(context));
    }

    [Fact]
    public void ConfigurationRowsCannotBeReferencedAsNumericOperands()
    {
        var rows = MeritFunctionCatalog.EvaluateAll(Context(), [Row("CONF", 1), Row("OPVA", 1)]);
        Assert.Empty(rows[0].Error); Assert.NotEmpty(rows[1].Error);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(2, 4)]
    [InlineData(4, 4)]
    [InlineData(7, 7)]
    public void ZoomThicknessReturnsRangeOrTargetAndUsesEveryConfiguration(double target, double expected)
    {
        var row = Row("ZTHI", 1, 1); row.Target = target; row.Weight = 2;
        var context = Context(); var rows = MeritFunctionCatalog.EvaluateAll(context, [Row("CONF", 1), row, Row("CONF", 2), row]); Success(rows);
        Assert.Equal(expected, rows[1].Value); Assert.Equal(expected, rows[3].Value);
        Assert.Equal(2 * Math.Pow(expected - target, 2), rows[1].Contribution);
        Assert.Equal(target, MeritFunctionCatalog.Evaluate(context.ActiveOptic, row).Value);
    }

    [Fact]
    public void ClosedIntervalIncludesLastThicknessAndPreservesItsSign()
    {
        var context = Context(); context.Configurations[0].SurfaceGroup.Items[2].Thickness = -2;
        context.Configurations[1].SurfaceGroup.Items[2].Thickness = 3;
        var row = Row("ZTHI", 1, 2); var rows = MeritFunctionCatalog.EvaluateAll(context, [row]); Success(rows);
        Assert.Equal(9, rows[0].Value);
        Assert.Equal(3, MeritFunctionCatalog.Evaluate(context.Configurations[0], Row("TTHI", 1, 2)).Value);
        Assert.Equal(-2, MeritFunctionCatalog.Evaluate(context.Configurations[0], Row("TTHI", 2, 2)).Value);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(1, 99, 0)]
    [InlineData(1, 1, -1)]
    [InlineData(1, 1, double.NaN)]
    public void InvalidZoomRangesOrTargetsFail(int first, int last, double target)
    {
        var row = Row("ZTHI", first, last); row.Target = target;
        Assert.NotEmpty(MeritFunctionCatalog.EvaluateAll(Context(), [row])[0].Error);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidThicknessInAnotherConfigurationCannotBeSilentlyDropped(double thickness)
    {
        var context = Context();
        if (!double.IsPositiveInfinity(thickness))
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => context.Configurations[0].SurfaceGroup.Items[1].Thickness = thickness);
            return;
        }
        context.Configurations[0].SurfaceGroup.Items[1].Thickness = thickness;
        Assert.NotEmpty(MeritFunctionCatalog.EvaluateAll(context, [Row("ZTHI", 1, 1)])[0].Error);
    }

    [Fact]
    public void ConcurrentSwitchingKeepsEachBatchAndTraceCacheIndependent()
    {
        var context = Context(); var before = Snapshot(context);
        var expected = context.Configurations.Select(optic => MeritFunctionCatalog.Evaluate(optic, Row("EFFL")).Value).ToArray();
        Parallel.For(0, 40, i =>
        {
            var number = i % 2 + 1;
            var rows = MeritFunctionCatalog.EvaluateAll(context, [Row("CONF", number), Row("EFFL"), Row("ZTHI", 1, 1)]); Success(rows);
            Near(expected[number - 1], rows[1].Value); Assert.Equal(4, rows[2].Value);
        });
        Assert.Equal(before, Snapshot(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DlsCandidatesPropagateOnlyUnbrokenBaseLinks(bool broken)
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var runtime = new WorkbenchRuntime(optic); runtime.AddMultiConfiguration();
        if (broken) runtime.SetMultiConfigurationThickness(1, 1, 9);
        runtime.ReplaceMeritFunction([Row("CONF", 1), Thickness(5), Row("CONF", 2), Thickness(9)]);
        var before = Snapshot(runtime.CreateMeritConfigurationContext());
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        var context = runtime.CreateMeritConfigurationContext();
        Assert.True(result.FinalMerit < result.InitialMerit); Near(broken ? 5 : 7, context.Configurations[0].SurfaceGroup.Items[1].Thickness, 1e-4);
        Near(broken ? 9 : 7, context.Configurations[1].SurfaceGroup.Items[1].Thickness, 1e-4);
        Assert.Equal(0, context.ActiveConfigurationIndex);
        Assert.True(runtime.Undo()); Assert.Equal(before, Snapshot(runtime.CreateMeritConfigurationContext())); Assert.True(runtime.Redo());
    }

    [Fact]
    public void DlsRecomputesPickupTargetsInsideOtherConfigurations()
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        optic.Pickups.SetThicknessPickup(1, 2, 2); optic.Pickups.ApplyAll();
        var runtime = new WorkbenchRuntime(optic); runtime.AddMultiConfiguration();
        var target = Row("TTHI", 2, 2); target.Target = 15;
        runtime.ReplaceMeritFunction([Row("CONF", 2), target]);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < 1e-8);
        foreach (var configuration in runtime.CreateMeritConfigurationContext().Configurations)
        {
            Near(7.5, configuration.SurfaceGroup.Items[1].Thickness, 1e-4);
            Near(15, configuration.SurfaceGroup.Items[2].Thickness, 1e-4);
        }
    }

    [Fact]
    public void ZthiDlsVariesOnlyTheActiveNonBaseConfiguration()
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var runtime = new WorkbenchRuntime(optic); runtime.AddMultiConfiguration();
        runtime.SetMultiConfigurationThickness(1, 1, 9); runtime.ActivateMultiConfiguration(1);
        var expected = optic.SurfaceGroup.Items[1].Thickness;
        runtime.ReplaceMeritFunction([Row("ZTHI", 1, 1)]);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < 1e-8);
        var context = runtime.CreateMeritConfigurationContext(); Assert.Equal(1, context.ActiveConfigurationIndex);
        Near(expected, context.ActiveOptic.SurfaceGroup.Items[1].Thickness, 1e-4);
        Near(expected, context.Configurations[0].SurfaceGroup.Items[1].Thickness);
    }

    [Fact]
    public void InvalidAndCancelledOptimizationLeaveAllConfigurationsUnchanged()
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var runtime = new WorkbenchRuntime(optic); runtime.AddMultiConfiguration(); runtime.ReplaceMeritFunction([Row("CONF", 3), Thickness()]);
        var before = Snapshot(runtime.CreateMeritConfigurationContext());
        Assert.Throws<OptimizationEvaluationException>(() => runtime.OptimizeMarkedVariables("Damped Least Squares", 4));
        Assert.Equal(before, Snapshot(runtime.CreateMeritConfigurationContext()));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var scope = ComputationCancellation.Push(cancelled.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.EvaluateAll(runtime.CreateMeritConfigurationContext(), [Row("CONF", 2)]));
        Assert.Equal(before, Snapshot(runtime.CreateMeritConfigurationContext()));
    }

    [Theory]
    [InlineData("CONF")]
    [InlineData("ZTHI")]
    public void NativeRowsRetainRawParametersAndNeverAutoUpgrade(string code)
    {
        var optic = OpticalFormatCatalog.Import($"MODE SEQ\nENPD 2\nWAVM 1 .55 1\nSURF 0\n DISZ INFINITY\nSURF 1\n STOP\n DISZ 5\nSURF 2\n DISZ 0\n{code} 1 1 0 0 0 0 .2 1 91 92\n", ".zmx");
        var row = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled); Assert.Contains("91 92", row.Comment);
    }

    [Fact]
    public async Task ApplicationReadsAllConfigurationsAndRoundTripsEditableSlotsWithUndo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"multi-merit-{Guid.NewGuid():N}.staropt");
        try
        {
            var context = Context(); var definitions = new[] { Row("CONF", 1), Thickness(1), Row("CONF", 2), Thickness(2), Row("ZTHI", 1, 1) };
            definitions[0].Weight = 999;
            foreach (var definition in definitions) context.ActiveOptic.MeritFunctionOperands.Add(definition);
            await StarOptProjectStore.SaveAsync(new(context.Configurations, 1), path);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            var rows = app.Optimization.GetMeritFunction(); Assert.All(rows, row => Assert.Empty(row.Error));
            Assert.Equal(5, rows[1].Value); Assert.Equal(9, rows[3].Value); Assert.Equal(4, rows[4].Value);
            Near(16.0 / 3, rows[1].Contribution); Assert.Equal(1, app.MultiConfiguration.GetRows().Single(r => r.Active).Index);
            var types = app.Optimization.GetMeritOperandTypes();
            var edits = rows.Select(row =>
            {
                var descriptor = types.Single(t => t.Code == row.Type);
                Assert.False(descriptor.CompatibilityOnly); Assert.Equal(6, descriptor.Parameters!.Count);
                var editor = new MeritOperandEditorRow(row, descriptor);
                if (row.Type == "CONF") editor.Parameter1 = 2;
                return editor.ToDto();
            }).ToArray();
            app.Optimization.SetMeritFunction(edits); Assert.Equal(9, app.Optimization.GetMeritFunction()[1].Value);
            Assert.True(app.Documents.Undo()); Assert.Equal(5, app.Optimization.GetMeritFunction()[1].Value); Assert.True(app.Documents.Redo());
            await app.Documents.SaveAsync(path); await app.Documents.OpenAsync(path);
            Assert.Equal(9, app.Optimization.GetMeritFunction()[1].Value); Assert.Equal(4, app.Optimization.GetMeritFunction()[4].Value);
            Assert.Equal(1, app.MultiConfiguration.GetRows().Single(r => r.Active).Index);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("CONF", ".zmx")]
    [InlineData("ZTHI", ".zmx")]
    [InlineData("CONF", ".seq")]
    [InlineData("ZTHI", ".seq")]
    [InlineData("CONF", ".len")]
    [InlineData("ZTHI", ".len")]
    [InlineData("CONF", ".txt")]
    [InlineData("ZTHI", ".txt")]
    public void LocalRowsCannotExportUnverifiedNativeColumns(string code, string extension)
    {
        var optic = Context().ActiveOptic; optic.MeritFunctionOperands.Add(Row(code, 1, 1));
        Assert.Contains("STAROPT", Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(optic, extension)).Message);
    }

    [Theory]
    [InlineData(".json")]
    [InlineData(".optiland")]
    [InlineData(".zmx")]
    public async Task SavingOnlyActiveOpticCannotLoseCrossConfigurationReferences(string extension)
    {
        var context = Context(); context.ActiveOptic.MeritFunctionOperands.Add(Row("CONF", 1));
        var path = Path.Combine(Path.GetTempPath(), $"configuration-export-{Guid.NewGuid():N}{extension}");
        await File.WriteAllTextAsync(path, "existing user file");
        try
        {
            var document = new LoadedOpticalDocument(context.ActiveOptic, context.Configurations, 1);
            await Assert.ThrowsAsync<NotSupportedException>(() => WorkbenchRuntime.SaveDocumentAsync(document, path));
            Assert.Equal("existing user file", await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidConfigurationContextsFailBeforeEvaluation()
    {
        Assert.Throws<ArgumentException>(() => new MeritConfigurationContext([]));
        Assert.Throws<ArgumentException>(() => new MeritConfigurationContext([null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeritConfigurationContext([new Optic()], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MeritConfigurationContext([new Optic()], 1));
    }

    private static MeritConfigurationContext Context()
    {
        var first = Optic.CreateCookeTriplet(); first.SurfaceGroup.Items[1].Thickness = 5; first.SurfaceGroup.Renumber();
        var second = Optic.FromSnapshot(first.ToSnapshot()); second.SurfaceGroup.Items[1].Thickness = 9; second.SurfaceGroup.Renumber();
        return new([first, second], 1);
    }
    private static MeritOperandDefinition Thickness(double target = 0) { var row = Row("TTHI", 1, 1); row.Target = target; return row; }
    private static MeritOperandDefinition Row(string code, int first = 0, int second = 0) => new()
    { Type = code, ZemaxIntegerParameters = [first, second], Weight = 1 };
    private static string Snapshot(MeritConfigurationContext context) => JsonSerializer.Serialize(context.Configurations.Select(o => o.ToSnapshot()),
        new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
    private static void Success(IEnumerable<MeritOperandEvaluation> rows) => Assert.All(rows, row => Assert.True(string.IsNullOrEmpty(row.Error), row.Error));
    private static void Near(double expected, double actual, double tolerance = 1e-10) => Assert.True(Math.Abs(expected - actual) <= tolerance, $"{expected:G17} != {actual:G17}");
}
