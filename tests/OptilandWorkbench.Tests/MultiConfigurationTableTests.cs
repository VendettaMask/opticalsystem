using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class MultiConfigurationTableTests
{
    [Theory]
    [InlineData(MultiConfigurationOperandKind.Thickness, 7)]
    [InlineData(MultiConfigurationOperandKind.Curvature, -.02)]
    [InlineData(MultiConfigurationOperandKind.Curvature, 0)]
    [InlineData(MultiConfigurationOperandKind.Conic, -1)]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter, 4)]
    public void TableBindingsReadAndEditActualConfigurationValues(MultiConfigurationOperandKind kind, double value)
    {
        var system = System(); system.ReplaceOperandRows([new(kind, 3)]);
        system.SetOperandValue(1, 1, value);
        var context = Context(system); var row = Mco("MCOV", 1, 2);
        Near(value, Eval(context, row)); Assert.NotEqual(value, Eval(context, Mco("MCOV", 1, 1)));
        Assert.Single(system.BrokenLinks);
        if (kind == MultiConfigurationOperandKind.Curvature)
            Assert.Equal(value == 0, system.Configurations[1].SurfaceGroup.Items[3].IsPlane);
        if (kind == MultiConfigurationOperandKind.SemiDiameter) Assert.True(system.Configurations[1].SurfaceGroup.Items[3].SemiDiameterFixed);
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(system.Configurations[1], row).Error);
    }

    [Theory]
    [InlineData("MCOV", 2, 8)]
    [InlineData("MCOV", 12, 8)]
    [InlineData("MCOG", 2, 2)]
    [InlineData("MCOG", 8, 8)]
    [InlineData("MCOG", 12, 8)]
    [InlineData("MCOL", 2, 8)]
    [InlineData("MCOL", 8, 8)]
    [InlineData("MCOL", 12, 12)]
    public void McoBoundaryContributionsHaveCorrectDirection(string code, double target, double expected)
    {
        var system = System(); system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 4)]); system.SetOperandValue(1, 1, 8);
        var row = Mco(code, 1, 2); row.Target = target; row.Weight = 3;
        var result = MeritFunctionCatalog.EvaluateAll(Context(system), [row])[0]; Assert.Empty(result.Error);
        Near(expected, result.Value); Near(3 * Math.Pow(expected - target, 2), result.Contribution);
    }

    [Fact]
    public void RowAndConfigurationReferencesAreIndependentOfSurfaceAndConfState()
    {
        var system = System(); system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 4), new(MultiConfigurationOperandKind.Curvature, 1)]);
        system.SetOperandValue(1, 0, 5); system.SetOperandValue(1, 1, 9);
        var rows = MeritFunctionCatalog.EvaluateAll(Context(system), [new() { Type = "CONF", ZemaxIntegerParameters = [2, 0] }, Mco("MCOV", 1, 1), Mco("MCOV", 1, 2), new() { Type = "DIFF", ZemaxIntegerParameters = [3, 2] }]);
        Assert.All(rows, row => Assert.Empty(row.Error)); Assert.Equal(5, rows[1].Value); Assert.Equal(9, rows[2].Value); Assert.Equal(4, rows[3].Value);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 3)]
    public void InvalidRowOrConfigurationFailsEvaluationAndOptimization(int row, int configuration)
    {
        var system = System(); system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 4)]);
        var definition = Mco("MCOV", row, configuration); var context = Context(system);
        Assert.NotEmpty(MeritFunctionCatalog.EvaluateAll(context, [definition])[0].Error);
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(context, [definition]));
    }

    [Fact]
    public void ReorderingRemapsEveryLocalReferenceIncludingDisabledRowsButPreservesNativeRows()
    {
        var system = System(); var first = new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, 4);
        var second = new MultiConfigurationOperand(MultiConfigurationOperandKind.Conic, 2); system.ReplaceOperandRows([first, second]);
        foreach (var optic in system.Configurations)
        {
            optic.MeritFunctionOperands.Add(Mco("MCOV", 1, 1));
            var disabled = Mco("MCOG", 2, 2); disabled.Enabled = false; optic.MeritFunctionOperands.Add(disabled);
            var native = Mco("MCOL", 39, 91); native.CompatibilityOnly = true; native.Enabled = false; optic.MeritFunctionOperands.Add(native);
        }
        var before = Eval(Context(system), system.Configurations[0].MeritFunctionOperands[0]);
        system.ReplaceOperandRows([second, first]);
        foreach (var optic in system.Configurations)
            Assert.Equal(new[] { 2, 1, 39 }, optic.MeritFunctionOperands.Select(row => row.ZemaxIntegerParameters[0]));
        Near(before, Eval(Context(system), system.Configurations[0].MeritFunctionOperands[0]));
        Assert.Throws<InvalidOperationException>(() => system.ReplaceOperandRows([second]));
        Assert.Equal(new[] { second, first }, system.OperandRows);
    }

    [Fact]
    public void SurfaceInsertionRemapsBindingAndReferencedSurfaceDeletionIsBlocked()
    {
        var system = System(); system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 4)]);
        var before = Eval(Context(system), Mco("MCOV", 1, 1));
        system.InsertSurface(2); Assert.Equal(5, system.OperandRows[0].SurfaceNumber); Near(before, Eval(Context(system), Mco("MCOV", 1, 1)));
        Assert.Throws<InvalidOperationException>(() => system.RemoveSurface(5)); Assert.Equal(9, system.Configurations[0].SurfaceGroup.Items.Count);
        system.RemoveSurface(2); Assert.Equal(4, system.OperandRows[0].SurfaceNumber); Near(before, Eval(Context(system), Mco("MCOV", 1, 1)));
        system.ReplaceOperandRows([new(MultiConfigurationOperandKind.SemiDiameter, 7)]); system.AddSurfaceBeforeImage(); Assert.Equal(8, system.OperandRows[0].SurfaceNumber);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    public void InvalidSurfaceBindingsCannotReplaceExistingTable(int surface)
    {
        var system = System(); var original = new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, 2); system.ReplaceOperandRows([original]);
        Assert.ThrowsAny<ArgumentException>(() => system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, surface)]));
        Assert.Equal(original, Assert.Single(system.OperandRows));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteCellEditsDoNotMutateValuesOrLinks(double value)
    {
        var system = System(); system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 2)]);
        var before = system.Configurations[1].SurfaceGroup.Items[2].Thickness;
        Assert.Throws<ArgumentOutOfRangeException>(() => system.SetOperandValue(1, 1, value));
        Assert.Empty(system.BrokenLinks); Assert.Equal(before, system.Configurations[1].SurfaceGroup.Items[2].Thickness);
    }

    [Fact]
    public void DuplicateUnknownAndNonFiniteBindingsFailAndPickupTargetsCannotBeEdited()
    {
        var system = System(); var row = new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, 2);
        Assert.Throws<ArgumentException>(() => system.ReplaceOperandRows([row, row]));
        Assert.Throws<ArgumentException>(() => system.ReplaceOperandRows([new((MultiConfigurationOperandKind)999, 2)]));
        system.Configurations[1].SurfaceGroup.Items[2].Thickness = double.PositiveInfinity;
        Assert.Throws<InvalidOperationException>(() => system.ReplaceOperandRows([row]));
        system.Configurations[1].SurfaceGroup.Items[2].Thickness = 2; system.ReplaceOperandRows([row]);
        system.Configurations[1].Pickups.SetThicknessPickup(1, 2, 2);
        Assert.Throws<InvalidOperationException>(() => system.SetOperandValue(1, 1, 3)); Assert.Empty(system.BrokenLinks);
        system.ReplaceOperandRows([new(MultiConfigurationOperandKind.SemiDiameter, 2)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => system.SetOperandValue(1, 1, -1)); Assert.Empty(system.BrokenLinks);
    }

    [Fact]
    public void BaseChangesUpdateLinkedCellsWhileExplicitOverridesStayIndependent()
    {
        var system = System(); system.AddConfiguration(); system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 2)]);
        system.SetOperandValue(1, 1, -3); system.SetOperandValue(1, 0, -2);
        Assert.Equal(new double[] { -2, -3, -2 }, system.Configurations.Select(optic => system.OperandRows[0].Read(optic)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionDlsReadsTableValuesFromCandidateConfigurationCopies(bool nonBase)
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[4].ThicknessVariable = true;
        var runtime = new WorkbenchRuntime(optic); runtime.AddMultiConfiguration();
        runtime.ReplaceMultiConfigurationOperands([new(MultiConfigurationOperandKind.Thickness, 4)]);
        if (nonBase) { runtime.SetMultiConfigurationOperandValue(1, 1, 8); runtime.ActivateMultiConfiguration(1); }
        var row = Mco("MCOV", 1, 2); row.Target = 6;
        runtime.ReplaceMeritFunction([row]);
        var before = runtime.CaptureDocument(); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < 1e-8); Near(6, Eval(runtime.CreateMeritConfigurationContext(), row), 1e-4);
        Near(nonBase ? before.Configurations[0].SurfaceGroup.Items[4].Thickness : 6,
            runtime.CreateMeritConfigurationContext().Configurations[0].SurfaceGroup.Items[4].Thickness, 1e-4);
        Assert.True(runtime.Undo()); Assert.Single(runtime.GetMultiConfigurationOperands()); Assert.True(runtime.Redo());
        Near(6, Eval(runtime.CreateMeritConfigurationContext(), row), 1e-4);
    }

    [Theory]
    [InlineData("MCOV")]
    [InlineData("MCOG")]
    [InlineData("MCOL")]
    public void NativeMcoRowsRemainDisabledWithoutGuessingMceOrder(string code)
    {
        var optic = OpticalFormatCatalog.Import($"MODE SEQ\nENPD 2\nWAVM 1 .55 1\nSURF 0\n DISZ INFINITY\nSURF 1\n STOP\n DISZ 5\nSURF 2\n DISZ 0\n{code} 17 2 0 0 0 0 .2 1 91 92\n", ".zmx");
        var row = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled); Assert.Equal(17, row.ZemaxIntegerParameters[0]); Assert.Contains("91 92", row.Comment);
    }

    [Fact]
    public async Task ApplicationTableEditingAndMcoSlotsRoundTripSaveUndoAndStaleRejection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mce-table-{Guid.NewGuid():N}.staropt");
        try
        {
            using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add();
            app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.Thickness, 4), new(MultiConfigurationParameterKind.SemiDiameter, 3)], app.Events.Revision);
            app.MultiConfiguration.SetOperandValue(1, 1, -2, app.Events.Revision);
            app.MultiConfiguration.SetOperandValue(2, 1, 4, app.Events.Revision);
            var stale = app.Events.Revision; app.MultiConfiguration.SetOperandValue(1, 0, 6, stale);
            Assert.Throws<InvalidOperationException>(() => app.MultiConfiguration.SetOperandValue(1, 1, 99, stale));
            var table = app.MultiConfiguration.GetOperandRows(); Assert.Equal(new double[] { 6, -2 }, table[0].Values);
            await app.Documents.SaveAsync(path);
            var project = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(2, project.OperandRows!.Count); Assert.Contains(project.BrokenLinks!, link => link.Property == "semiDiameter");
            foreach (var code in new[] { "MCOV", "MCOG", "MCOL" }) project.Configurations[0].MeritFunctionOperands.Add(Mco(code, 1, 2));
            await StarOptProjectStore.SaveAsync(project, path); await app.Documents.OpenAsync(path);
            var types = app.Optimization.GetMeritOperandTypes();
            var edits = app.Optimization.GetMeritFunction().Select(row =>
            {
                var type = types.Single(type => type.Code == row.Type); Assert.False(type.CompatibilityOnly); Assert.Equal(6, type.Parameters!.Count);
                var editor = new MeritOperandEditorRow(row, type) { Parameter1 = 2 }; return editor.ToDto();
            }).ToArray();
            app.Optimization.SetMeritFunction(edits); Assert.Equal(4, app.Optimization.GetMeritFunction()[0].Value);
            app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.SemiDiameter, 3), new(MultiConfigurationParameterKind.Thickness, 4)], app.Events.Revision);
            Assert.Equal(1, app.Optimization.GetMeritFunction()[0].ZemaxInt1); Assert.Equal(4, app.Optimization.GetMeritFunction()[0].Value);
            Assert.True(app.Documents.Undo()); Assert.Equal(2, app.Optimization.GetMeritFunction()[0].ZemaxInt1); Assert.True(app.Documents.Redo());
            await app.Documents.SaveAsync(path); await app.Documents.OpenAsync(path);
            Assert.Equal(4, app.Optimization.GetMeritFunction()[0].Value); Assert.Equal(MultiConfigurationParameterKind.SemiDiameter, app.MultiConfiguration.GetOperandRows()[0].Kind);
            var revision = app.Events.Revision;
            Assert.Throws<InvalidOperationException>(() => app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.Thickness, 4)], revision));
            Assert.Equal(revision, app.Events.Revision);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OlderProjectVersionsLoadAnEmptyOperandTable(int version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"legacy-mce-{Guid.NewGuid():N}.staropt");
        try
        {
            await File.WriteAllBytesAsync(path, Container(version, null));
            var project = await StarOptProjectStore.LoadAsync(path); Assert.Null(project.OperandRows);
            var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet()); await runtime.LoadAsync(path); Assert.Empty(runtime.GetMultiConfigurationOperands());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 99)]
    public async Task MalformedProjectRowTablesAreRejected(int kind, int surface)
    {
        var path = Path.Combine(Path.GetTempPath(), $"invalid-mce-{Guid.NewGuid():N}.staropt");
        try
        {
            await File.WriteAllBytesAsync(path, Container(5, new[] { new MultiConfigurationOperand((MultiConfigurationOperandKind)kind, surface) }));
            await Assert.ThrowsAsync<InvalidDataException>(() => StarOptProjectStore.LoadAsync(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ProductionDlsReadsCurvatureTableInInverseMillimeters()
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[1].RadiusVariable = true;
        var runtime = new WorkbenchRuntime(optic); runtime.AddMultiConfiguration();
        runtime.ReplaceMultiConfigurationOperands([new(MultiConfigurationOperandKind.Curvature, 1)]);
        var row = Mco("MCOV", 1, 2); row.Target = .03; runtime.ReplaceMeritFunction([row]);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < 1e-10);
        Near(.03, Eval(runtime.CreateMeritConfigurationContext(), row), 1e-6);
    }

    [Fact]
    public async Task OldVersionCannotSmuggleNewRowTableSemantics()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wrong-version-mce-{Guid.NewGuid():N}.staropt");
        try
        {
            await File.WriteAllBytesAsync(path, Container(4, new[] { new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, 1) }));
            await Assert.ThrowsAsync<InvalidDataException>(() => StarOptProjectStore.LoadAsync(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(".json")]
    [InlineData(".zmx")]
    public async Task AnyNonemptyTablePreventsLossyExportEvenWithoutMeritRows(string extension)
    {
        var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet()); runtime.ReplaceMultiConfigurationOperands([new(MultiConfigurationOperandKind.Thickness, 2)]);
        var path = Path.Combine(Path.GetTempPath(), $"mce-export-{Guid.NewGuid():N}{extension}");
        await Assert.ThrowsAsync<NotSupportedException>(() => runtime.SaveAsync(path)); Assert.False(File.Exists(path));
    }

    private static MultiConfiguration System() { var system = new MultiConfiguration(Optic.CreateCookeTriplet()); system.AddConfiguration(); return system; }
    private static MeritConfigurationContext Context(MultiConfiguration system) => new(system.Configurations, 0, system.OperandRows);
    private static MeritOperandDefinition Mco(string type, int row, int configuration) => new() { Type = type, ZemaxIntegerParameters = [row, configuration], Weight = 1 };
    private static double Eval(MeritConfigurationContext context, MeritOperandDefinition row)
    { var result = MeritFunctionCatalog.EvaluateAll(context, [row])[0]; Assert.Empty(result.Error); return result.Value; }
    private static void Near(double expected, double actual, double tolerance = 1e-10) => Assert.True(Math.Abs(expected - actual) <= tolerance, $"{expected:G17} != {actual:G17}");
    private static byte[] Container(int version, IReadOnlyList<MultiConfigurationOperand>? rows)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            FormatVersion = version,
            Application = "Optical System Design",
            ActiveConfigurationIndex = 0,
            Configurations = new[] { Optic.CreateCookeTriplet().ToSnapshot() },
            OperandRows = rows
        },
            new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, true)) brotli.Write(payload);
        var compressed = output.ToArray(); var bytes = new byte[52 + compressed.Length]; "STAROPT\x1a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), StarOptProjectStore.ContainerVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), compressed.Length);
        SHA256.HashData(payload).CopyTo(bytes, 20); compressed.CopyTo(bytes, 52); return bytes;
    }
}
