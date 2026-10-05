using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class MultiConfigurationVariableTests
{
    [Theory]
    [InlineData(MultiConfigurationOperandKind.Thickness)]
    [InlineData(MultiConfigurationOperandKind.Curvature)]
    [InlineData(MultiConfigurationOperandKind.Conic)]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter)]
    public void VariablesRemainIndependentEvenWhenEqualToBase(MultiConfigurationOperandKind kind)
    {
        var system = System(kind); var row = system.OperandRows[0]; var initial = row.Read(system.Configurations[1]);
        system.SetOperandVariable(1, 1, true); system.UpdateLinkState(1, 3, row.Property);
        system.SetOperandValue(1, 0, initial + .01);
        Assert.Equal(initial, row.Read(system.Configurations[1])); Assert.Single(system.BrokenLinks);
        system.SetOperandVariable(1, 1, false); system.SetOperandValue(1, 0, initial + .02);
        Assert.Equal(initial, row.Read(system.Configurations[1])); Assert.Empty(system.OperandVariables);
    }

    [Fact]
    public void ReorderingInsertionAndCloningPreserveVariableIdentity()
    {
        var system = System(MultiConfigurationOperandKind.Thickness); var first = system.OperandRows[0];
        var second = new MultiConfigurationOperand(MultiConfigurationOperandKind.Conic, 2);
        system.ReplaceOperandRows([first, second]); system.SetOperandVariable(1, 1, true);
        system.ReplaceOperandRows([second, first]); Assert.Equal(first, Assert.Single(system.OperandVariables).Operand);
        system.InsertSurface(2); Assert.Equal(4, Assert.Single(system.OperandVariables).Operand.SurfaceNumber);
        system.RemoveSurface(2); Assert.Equal(first, Assert.Single(system.OperandVariables).Operand);
        system.AddConfiguration(1); Assert.Equal(new[] { 1, 2 }, system.OperandVariables.Select(v => v.ConfigurationIndex));
        system.ReplaceOperandRows([second]); Assert.Empty(system.OperandVariables);
    }

    [Theory]
    [InlineData(MultiConfigurationOperandKind.Thickness)]
    [InlineData(MultiConfigurationOperandKind.Curvature)]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter)]
    public void PickupTargetsCannotBecomeIndependentVariables(MultiConfigurationOperandKind kind)
    {
        var system = System(kind); var optic = system.Configurations[1];
        if (kind == MultiConfigurationOperandKind.Thickness) optic.Pickups.SetThicknessPickup(1, 3, 1);
        else if (kind == MultiConfigurationOperandKind.Curvature) optic.Pickups.SetCurvaturePickup(1, 3, 1);
        else optic.Pickups.SetSemiDiameterPickup(1, 3, 1);
        Assert.Throws<InvalidOperationException>(() => system.SetOperandVariable(1, 1, true));
        Assert.Empty(system.OperandVariables); Assert.Empty(system.BrokenLinks);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public void InvalidCellDoesNotChangeVariables(int row, int configuration)
    {
        var system = System(MultiConfigurationOperandKind.Thickness);
        Assert.Throws<ArgumentOutOfRangeException>(() => system.SetOperandVariable(row, configuration, true));
        Assert.Empty(system.OperandVariables); Assert.Empty(system.BrokenLinks);
    }

    [Theory]
    [InlineData(MultiConfigurationOperandKind.Thickness, -2, 6)]
    [InlineData(MultiConfigurationOperandKind.Curvature, 0, -.02)]
    [InlineData(MultiConfigurationOperandKind.Conic, -.5, .5)]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter, 3, 4)]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter, .1, 4)]
    public void ProductionDlsJointlyOptimizesIndependentCellsAndRetainsLinkedThirdConfiguration(
        MultiConfigurationOperandKind kind, double target1, double target2)
    {
        var runtime = Runtime(kind); runtime.AddMultiConfiguration();
        runtime.SetMultiConfigurationOperandVariable(1, 0, true); runtime.SetMultiConfigurationOperandVariable(1, 1, true);
        runtime.ActivateMultiConfiguration(1); runtime.ReplaceMeritFunction([Mco(1, target1), Mco(2, target2)]);
        var initial = runtime.CaptureDocument(); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 60);
        Assert.True(result.FinalMerit < 1e-8, result.FinalMerit.ToString("G17"));
        var context = runtime.CreateMeritConfigurationContext(); Assert.Equal(1, context.ActiveConfigurationIndex);
        Near(target1, context.OperandRows[0].Read(context.Configurations[0])); Near(target2, context.OperandRows[0].Read(context.Configurations[1]));
        Near(target1, context.OperandRows[0].Read(context.Configurations[2]));
        var evaluated = MeritFunctionCatalog.EvaluateAll(context, runtime.CurrentOptic.MeritFunctionOperands);
        Near(result.FinalMerit, evaluated.Sum(r => r.Contribution) / 2, 1e-10);
        Assert.True(runtime.Undo()); Assert.Equal(initial.OperandVariables, runtime.CaptureDocument().OperandVariables);
        Assert.True(runtime.Redo()); Near(target2, runtime.GetMultiConfigurationOperands()[0].Read(runtime.CurrentOptic));
    }

    [Fact]
    public void ActiveLdeVariableIsDeduplicatedAndUnusedClonedFlagsAreNotActivated()
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[3].ThicknessVariable = true;
        var runtime = new WorkbenchRuntime(optic); runtime.AddMultiConfiguration();
        runtime.ReplaceMultiConfigurationOperands([new(MultiConfigurationOperandKind.Thickness, 3)]);
        runtime.SetMultiConfigurationOperandVariable(1, 0, true);
        Assert.Single(runtime.GetMarkedOptimizationVariables());
        runtime.SetMultiConfigurationOperandVariable(1, 1, true); Assert.Equal(2, runtime.GetMarkedOptimizationVariables().Count);
        runtime.ReplaceMeritFunction([Mco(1, 2), Mco(2, 3)]);
        Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 40).FinalMerit < 1e-8);
        runtime.SetMultiConfigurationOperandVariable(1, 0, false); Assert.False(runtime.CurrentOptic.SurfaceGroup.Items[3].ThicknessVariable);
        Assert.Single(runtime.GetMarkedOptimizationVariables());
    }

    [Fact]
    public void OptimizationFailureRollsBackAllConfigurationsAndVariableMetadata()
    {
        var runtime = Runtime(MultiConfigurationOperandKind.Thickness);
        runtime.SetMultiConfigurationOperandVariable(1, 0, true); runtime.SetMultiConfigurationOperandVariable(1, 1, true);
        runtime.ReplaceMeritFunction([Mco(3, 9)]); var before = runtime.CaptureDocument();
        Assert.ThrowsAny<Exception>(() => runtime.OptimizeMarkedVariables("Damped Least Squares", 10));
        var after = runtime.CaptureDocument(); Assert.Equal(before.OperandVariables, after.OperandVariables); Assert.Equal(before.BrokenLinks, after.BrokenLinks);
        Assert.Equal(before.Configurations.Select(o => o.SurfaceGroup.Items[3].Thickness), after.Configurations.Select(o => o.SurfaceGroup.Items[3].Thickness));
    }

    [Fact]
    public async Task ApplicationJointRunSaveClearUndoAndStaleProtectionAgree()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mce-vars-{Guid.NewGuid():N}.staropt");
        try
        {
            var runtime = Runtime(MultiConfigurationOperandKind.Thickness);
            runtime.SetMultiConfigurationOperandVariable(1, 0, true); runtime.SetMultiConfigurationOperandVariable(1, 1, true);
            runtime.ReplaceMeritFunction([Mco(1, 2), Mco(2, 3)]); await runtime.SaveAsync(path);
            using var app = WorkbenchApplication.Create("cooke"); await app.Documents.OpenAsync(path);
            Assert.Equal(new[] { true, true }, app.MultiConfiguration.GetOperandRows()[0].Variables);
            Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
            var result = await app.Optimization.OptimizeVariablesAsync("Damped Least Squares", 50);
            Assert.Equal(2, result.Variables.Count); Assert.Equal(new[] { 0, 1 }, result.Variables.Select(v => v.ConfigurationIndex));
            Near(2, result.Variables[0].FinalValue); Near(3, result.Variables[1].FinalValue);
            var stale = app.Events.Revision; app.MultiConfiguration.SetOperandVariable(1, 1, false, stale);
            Assert.Throws<InvalidOperationException>(() => app.MultiConfiguration.SetOperandVariable(1, 0, false, stale));
            Assert.True(app.Documents.Undo()); Assert.All(app.MultiConfiguration.GetOperandRows()[0].Variables, Assert.True);
            app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll);
            Assert.Empty(app.Optimization.GetMarkedVariables()); Assert.All(app.MultiConfiguration.GetOperandRows()[0].Variables, Assert.False);
            Assert.True(app.Documents.Undo()); Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
            await app.Documents.SaveAsync(path); var project = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(2, project.OperandVariables!.Count);
            await app.Documents.OpenAsync(path); Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task LegacyProjectsDoNotAcquireVariables(int version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mce-legacy-{Guid.NewGuid():N}.staropt");
        try { await File.WriteAllBytesAsync(path, Container(version, null)); Assert.Null((await StarOptProjectStore.LoadAsync(path)).OperandVariables); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(5, 0, 3)] // metadata cannot be smuggled into an older version
    [InlineData(6, -1, 3)]
    [InlineData(6, 2, 3)]
    [InlineData(6, 0, 4)] // no such row binding
    public async Task MalformedOrWrongVersionVariableMetadataIsRejected(int version, int configuration, int surface)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mce-invalid-{Guid.NewGuid():N}.staropt");
        try
        {
            await File.WriteAllBytesAsync(path, Container(version, [new(configuration, new(MultiConfigurationOperandKind.Thickness, surface))]));
            await Assert.ThrowsAsync<InvalidDataException>(() => StarOptProjectStore.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DuplicateVariableMetadataIsRejectedAndSemiDiameterIsPinned()
    {
        var system = System(MultiConfigurationOperandKind.SemiDiameter);
        var variable = new MultiConfigurationVariable(1, system.OperandRows[0]);
        Assert.Throws<ArgumentException>(() => new MultiConfiguration(system.Configurations, operandRows: system.OperandRows, operandVariables: [variable, variable]));
        system.SetOperandVariable(1, 1, true); Assert.True(system.Configurations[1].SurfaceGroup.Items[3].SemiDiameterFixed);
    }

    [Theory]
    [InlineData(MultiConfigurationOperandKind.Thickness)]
    [InlineData(MultiConfigurationOperandKind.Curvature)]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter)]
    public void ReplacingSolveClearsCellVariableAndUndoRestoresIt(MultiConfigurationOperandKind kind)
    {
        var runtime = Runtime(kind); runtime.SetMultiConfigurationOperandVariable(1, 0, true);
        if (kind == MultiConfigurationOperandKind.Thickness)
        {
            Assert.Equal(ThicknessSolveKind.Variable, runtime.GetThicknessSolve(3).Kind);
            runtime.SetThicknessSolve(3, new(ThicknessSolveKind.Pickup, 1, 1));
        }
        else if (kind == MultiConfigurationOperandKind.Curvature)
        {
            Assert.Equal(RadiusSolveKind.Variable, runtime.GetRadiusSolve(3).Kind);
            runtime.SetRadiusSolve(3, new(RadiusSolveKind.Pickup, 1, 1));
        }
        else runtime.SetSemiDiameterSolve(3, new(SemiDiameterSolveKind.Automatic));
        Assert.Empty(runtime.GetMultiConfigurationVariables()); Assert.True(runtime.Undo());
        Assert.Single(runtime.GetMultiConfigurationVariables());
    }

    [Fact]
    public void ProductionDlsHandlesCoupledRowsAcrossConfBoundaries()
    {
        var runtime = Runtime(MultiConfigurationOperandKind.Thickness);
        runtime.SetMultiConfigurationOperandVariable(1, 0, true); runtime.SetMultiConfigurationOperandVariable(1, 1, true);
        runtime.ReplaceMeritFunction([
            new() { Type = "CONF", ZemaxIntegerParameters = [1, 0] },
            new() { Type = "TTHI", ZemaxIntegerParameters = [3, 3], Target = 2, Weight = 1 },
            new() { Type = "CONF", ZemaxIntegerParameters = [2, 0] },
            new() { Type = "TTHI", ZemaxIntegerParameters = [3, 3], Weight = 0 },
            new() { Type = "DIFF", ZemaxIntegerParameters = [4, 2], Target = 1, Weight = 1 }]);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 60);
        Assert.True(result.FinalMerit < 1e-8);
        var context = runtime.CreateMeritConfigurationContext(); Near(2, context.OperandRows[0].Read(context.Configurations[0]));
        Near(3, context.OperandRows[0].Read(context.Configurations[1]));
    }

    [Fact]
    public async Task CoatingOnlyVariablesAreVisibleAndRunThroughTheApplicationService()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coating-variable-{Guid.NewGuid():N}.staropt");
        try
        {
            var optic = Optic.CreateCookeTriplet();
            optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([
                new(new ConstantIndexMaterial("film", 1.8), 100, new(MultiplierVariable: true))]);
            optic.MeritFunctionOperands.Add(new() { Type = "CMVA", ZemaxIntegerParameters = [1, 1], Target = 1.6, Weight = 1 });
            await new WorkbenchRuntime(optic).SaveAsync(path);
            using var app = WorkbenchApplication.Create("cooke"); await app.Documents.OpenAsync(path);
            Assert.Equal(OptimizationVariableKind.CoatingMultiplier, Assert.Single(app.Optimization.GetMarkedVariables()).Kind);
            var result = await app.Optimization.OptimizeVariablesAsync("Damped Least Squares", 40);
            Near(1.6, Assert.Single(result.Variables).FinalValue); Assert.True(result.FinalMerit < 1e-8);
        }
        finally { File.Delete(path); }
    }

    private static MultiConfiguration System(MultiConfigurationOperandKind kind)
    { var system = new MultiConfiguration(Optic.CreateCookeTriplet()); system.AddConfiguration(); system.ReplaceOperandRows([new(kind, 3)]); return system; }
    private static WorkbenchRuntime Runtime(MultiConfigurationOperandKind kind)
    { var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet()); runtime.AddMultiConfiguration(); runtime.ReplaceMultiConfigurationOperands([new(kind, 3)]); return runtime; }
    private static MeritOperandDefinition Mco(int configuration, double target) => new() { Type = "MCOV", ZemaxIntegerParameters = [1, configuration], Target = target, Weight = 1 };
    private static void Near(double expected, double actual, double tolerance = 1e-4) => Assert.True(Math.Abs(expected - actual) <= tolerance, $"{expected:G17} != {actual:G17}");
    private static byte[] Container(int version, IReadOnlyList<MultiConfigurationVariable>? variables)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            FormatVersion = version,
            Application = "Optical System Design",
            ActiveConfigurationIndex = 0,
            Configurations = new[] { Optic.CreateCookeTriplet().ToSnapshot() },
            OperandRows = version >= 5 ? new[] { new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, 3) } : null,
            OperandVariables = variables
        }, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        using var output = new MemoryStream(); using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, true)) brotli.Write(payload);
        var compressed = output.ToArray(); var bytes = new byte[52 + compressed.Length]; "STAROPT\x1a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), StarOptProjectStore.ContainerVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), compressed.Length); SHA256.HashData(payload).CopyTo(bytes, 20); compressed.CopyTo(bytes, 52); return bytes;
    }
}
