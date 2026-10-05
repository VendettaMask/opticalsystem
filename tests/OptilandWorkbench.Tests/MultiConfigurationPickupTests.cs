using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Multiconfig;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class MultiConfigurationPickupTests
{
    [Theory]
    [InlineData(MultiConfigurationOperandKind.Thickness, 4, 2, 1, 9)]
    [InlineData(MultiConfigurationOperandKind.Curvature, .04, -2, .01, -.07)]
    [InlineData(MultiConfigurationOperandKind.Curvature, .04, 0, 0, 0)]
    [InlineData(MultiConfigurationOperandKind.Conic, -.5, 2, .3, -.7)]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter, 3, 2, 1, 7)]
    public void FourKindsUseEditorCoordinatesAndClearRetainsCurrentValue(MultiConfigurationOperandKind kind, double source, double scale, double offset, double expected)
    {
        var system = System(kind); system.SetOperandValue(1, 0, source);
        system.SetOperandPickup(1, 1, 1, 0, scale, offset);
        Near(expected, Value(system, 1)); Assert.Single(system.OperandPickups);
        Assert.Throws<InvalidOperationException>(() => system.SetOperandValue(1, 1, 8));
        Assert.Throws<InvalidOperationException>(() => system.SetOperandVariable(1, 1, true));
        system.ClearOperandPickup(1, 1); system.SetOperandValue(1, 0, source + .1);
        Near(expected, Value(system, 1)); Assert.Empty(system.OperandPickups); Assert.NotEmpty(system.BrokenLinks);
    }

    [Fact]
    public void ChainCombinesMceSurfaceAndBaseLinksInDependencyOrder()
    {
        var system = System(MultiConfigurationOperandKind.Thickness); system.AddConfiguration(); system.AddConfiguration();
        system.Configurations[1].Pickups.SetThicknessPickup(3, 4, 2);
        system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 3), new(MultiConfigurationOperandKind.Thickness, 4)]);
        system.SetOperandPickup(1, 1, 1, 0, 2, 1);
        system.SetOperandPickup(2, 2, 2, 1, 3);
        system.SetOperandValue(1, 0, 4);
        Near(9, Value(system, 1)); Near(18, system.Configurations[1].SurfaceGroup.Items[4].Thickness);
        Near(54, system.Configurations[2].SurfaceGroup.Items[4].Thickness); Near(4, Value(system, 3));
    }

    [Fact]
    public void CrossKindRowsAndBackFocusSourceUseUpdatedValues()
    {
        var system = System(MultiConfigurationOperandKind.Conic);
        system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Conic, 3), new(MultiConfigurationOperandKind.Thickness, 4)]);
        system.SetOperandValue(1, 0, -.5); system.SetOperandPickup(2, 0, 1, 0, -2, 1);
        Near(2, system.Configurations[0].SurfaceGroup.Items[4].Thickness);
        system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 7), new(MultiConfigurationOperandKind.Conic, 4)]);
        system.Configurations[0].Solves.DesiredBackFocus = 100;
        system.SetOperandPickup(2, 1, 1, 0, .1);
        var optic = system.Configurations[0]; var expected = Math.Max(0, 100 - optic.SurfaceGroup.Items.Skip(1).Take(6).Sum(s => s.Thickness));
        Near(expected, optic.SurfaceGroup.Items[7].Thickness); Near(.1 * expected, system.Configurations[1].SurfaceGroup.Items[4].Conic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclesThroughSurfaceOrBackFocusSolvesAreRejectedAtomically(bool backFocus)
    {
        var system = System(MultiConfigurationOperandKind.Thickness);
        system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, backFocus ? 7 : 3), new(MultiConfigurationOperandKind.Thickness, 1)]);
        if (!backFocus)
        {
            system.Configurations[0].Pickups.SetThicknessPickup(1, 3, 1);
            system.Configurations[0].Pickups.ApplyAll(); // Start from the evaluated prescription used by the application.
        }
        var before = system.Configurations.Select(o => o.SurfaceGroup.Items.Select(s => s.Thickness).ToArray()).ToArray();
        var error = Assert.Throws<InvalidOperationException>(() => system.SetOperandPickup(2, 0, 1, 0)); Assert.Contains("循环", error.Message);
        Assert.Empty(system.OperandPickups); Assert.Empty(system.BrokenLinks);
        for (var i = 0; i < before.Length; i++) Assert.Equal(before[i], system.Configurations[i].SurfaceGroup.Items.Select(s => s.Thickness));
    }

    [Theory]
    [InlineData(0, 1, 1, 0)]
    [InlineData(1, -1, 1, 0)]
    [InlineData(1, 2, 1, 0)]
    [InlineData(1, 1, 2, 0)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 1, 1, -1)]
    public void InvalidCellReferencesAndForwardReferencesDoNotCreateSolves(int row, int configuration, int sourceRow, int sourceConfiguration)
    {
        var system = System(MultiConfigurationOperandKind.Thickness);
        system.ReplaceOperandRows([system.OperandRows[0], new(MultiConfigurationOperandKind.Thickness, 4)]);
        Assert.ThrowsAny<ArgumentException>(() => system.SetOperandPickup(row, configuration, sourceRow, sourceConfiguration));
        Assert.Empty(system.OperandPickups); Assert.Empty(system.BrokenLinks);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(1, double.NegativeInfinity)]
    public void NonfiniteSolveParametersAreRejected(double scale, double offset)
    {
        var system = System(MultiConfigurationOperandKind.Thickness);
        Assert.ThrowsAny<ArgumentException>(() => system.SetOperandPickup(1, 1, 1, 0, scale, offset)); Assert.Empty(system.OperandPickups);
    }

    [Theory]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter, 1, -100)]
    [InlineData(MultiConfigurationOperandKind.Thickness, 1e308, 1e308)]
    public void InvalidComputedValuePreservesPreviousVariablesAndLinks(MultiConfigurationOperandKind kind, double scale, double offset)
    {
        var system = System(kind); system.SetOperandVariable(1, 1, true); var before = Value(system, 1); var links = system.BrokenLinks;
        Assert.Throws<InvalidOperationException>(() => system.SetOperandPickup(1, 1, 1, 0, scale, offset));
        Near(before, Value(system, 1)); Assert.Single(system.OperandVariables); Assert.Equal(links, system.BrokenLinks); Assert.Empty(system.OperandPickups);
    }

    [Fact]
    public void SourceEditThatWouldInvalidateDependentCellRollsBackTheWholeChange()
    {
        var system = System(MultiConfigurationOperandKind.SemiDiameter); system.SetOperandValue(1, 0, 3);
        system.SetOperandPickup(1, 1, 1, 0, 1, -2); var before = system.BrokenLinks;
        Assert.Throws<InvalidOperationException>(() => system.SetOperandValue(1, 0, 1));
        Near(3, Value(system, 0)); Near(1, Value(system, 1)); Assert.Equal(before, system.BrokenLinks);
    }

    [Fact]
    public void ReorderingDeletionInsertionAndCloningKeepReferencesOrRejectInvalidOrder()
    {
        var system = System(MultiConfigurationOperandKind.Thickness); var source = system.OperandRows[0]; var target = new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, 4);
        system.ReplaceOperandRows([source, target]); system.SetOperandPickup(2, 1, 1, 0, 2, 1);
        Assert.Throws<ArgumentException>(() => system.ReplaceOperandRows([target, source])); Assert.Equal(source, system.OperandRows[0]);
        Assert.Throws<ArgumentException>(() => system.ReplaceOperandRows([target])); Assert.Equal(2, system.OperandRows.Count);
        system.InsertSurface(2); Assert.Equal(4, system.OperandPickups[0].SourceOperand.SurfaceNumber); Assert.Equal(5, system.OperandPickups[0].Operand.SurfaceNumber);
        system.RemoveSurface(2); Assert.Equal(source, system.OperandPickups[0].SourceOperand);
        var copy = system.AddConfiguration(1); Assert.Equal(0, system.OperandPickups.Single(p => p.ConfigurationIndex == copy).SourceConfigurationIndex);
        system.ReplaceOperandRows([source]); Assert.Empty(system.OperandPickups);
    }

    [Fact]
    public void CopyingAConfigurationRemapsItsInternalPickupToTheNewConfiguration()
    {
        var system = System(MultiConfigurationOperandKind.Thickness);
        system.ReplaceOperandRows([system.OperandRows[0], new(MultiConfigurationOperandKind.Thickness, 4)]);
        system.SetOperandPickup(2, 0, 1, 0, 2); var copy = system.AddConfiguration(0);
        Assert.Equal(copy, system.OperandPickups.Single(p => p.ConfigurationIndex == copy).SourceConfigurationIndex);
        system.SetOperandValue(1, copy, 5); Near(10, system.Configurations[copy].SurfaceGroup.Items[4].Thickness);
    }

    [Theory]
    [InlineData(MultiConfigurationOperandKind.Thickness, 4)]
    [InlineData(MultiConfigurationOperandKind.Curvature, .02)]
    [InlineData(MultiConfigurationOperandKind.Conic, -.3)]
    [InlineData(MultiConfigurationOperandKind.SemiDiameter, 3)]
    public void ProductionDlsOptimizesSourceThroughDependentConfiguration(MultiConfigurationOperandKind kind, double desiredSource)
    {
        var runtime = Runtime(kind); runtime.SetMultiConfigurationOperandVariable(1, 0, true);
        runtime.SetMultiConfigurationOperandPickup(1, 1, 1, 0, 2, .1); runtime.ActivateMultiConfiguration(1);
        runtime.ReplaceMeritFunction([Mco(2, 2 * desiredSource + .1)]);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 60);
        Assert.True(result.FinalMerit < 1e-8, result.FinalMerit.ToString("G17"));
        var context = runtime.CreateMeritConfigurationContext(); Near(desiredSource, context.OperandRows[0].Read(context.Configurations[0]), 1e-4);
        Near(2 * desiredSource + .1, context.OperandRows[0].Read(context.Configurations[1]), 1e-4);
        Near(result.FinalMerit, MeritFunctionCatalog.EvaluateAll(context, runtime.CurrentOptic.MeritFunctionOperands).Single().Contribution);
        Assert.True(runtime.Undo()); Assert.Single(runtime.GetMultiConfigurationPickups()); Assert.True(runtime.Redo());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptimizingThenEditingSurfacesDoesNotApplyStructuralChangesTwice(bool marked)
    {
        var runtime = Runtime(MultiConfigurationOperandKind.Curvature); runtime.SetMultiConfigurationOperandVariable(1, 0, true);
        runtime.ReplaceMeritFunction([Mco(1, .02)]);
        if (marked) runtime.OptimizeMarkedVariables("Damped Least Squares", 15);
        else runtime.OptimizeSurfaceRadius(runtime.CurrentOptic.SurfaceGroup.Items[3], "Damped Least Squares", 1);
        runtime.AddSurface(); Assert.All(runtime.CaptureDocument().Configurations, o => Assert.Equal(9, o.SurfaceGroup.Items.Count));
        runtime.InsertSurface(1, false); Assert.All(runtime.CaptureDocument().Configurations, o => Assert.Equal(10, o.SurfaceGroup.Items.Count));
        runtime.RemoveSurface(runtime.CurrentOptic.SurfaceGroup.Items[1]); Assert.All(runtime.CaptureDocument().Configurations, o => Assert.Equal(9, o.SurfaceGroup.Items.Count));
    }

    [Fact]
    public async Task ApplicationSaveUndoStaleAndSolveReplacementPreservePickupMeaning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mce-pickups-{Guid.NewGuid():N}.staropt");
        try
        {
            using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add();
            app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.Thickness, 3)], app.Events.Revision);
            app.MultiConfiguration.SetOperandValue(1, 0, 4, app.Events.Revision);
            app.MultiConfiguration.SetOperandVariable(1, 1, true, app.Events.Revision);
            var revision = app.Events.Revision; app.MultiConfiguration.SetOperandPickup(1, 1, new(1, 0, 2, 1), revision);
            Assert.Throws<InvalidOperationException>(() => app.MultiConfiguration.SetOperandPickup(1, 1, null, revision));
            Assert.False(app.MultiConfiguration.GetOperandRows()[0].Variables[1]); Near(9, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
            app.MultiConfiguration.Activate(1); app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.SetAllThicknesses);
            Assert.DoesNotContain(app.Optimization.GetMarkedVariables(), v => v.ConfigurationIndex == 1 && v.SurfaceNumber == 3);
            app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll); Assert.NotNull(app.MultiConfiguration.GetOperandRows()[0].Pickups[1]);
            await app.Documents.SaveAsync(path); await app.Documents.OpenAsync(path);
            Assert.NotNull(app.MultiConfiguration.GetOperandRows()[0].Pickups[1]);
            app.Prescription.SetThicknessSolve(3, new(ThicknessSolveKind.Fixed)); Assert.Null(app.MultiConfiguration.GetOperandRows()[0].Pickups[1]);
            Assert.True(app.Documents.Undo()); Assert.NotNull(app.MultiConfiguration.GetOperandRows()[0].Pickups[1]);
            app.MultiConfiguration.SetOperandValue(1, 0, 5, app.Events.Revision); Near(11, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
            await app.Documents.SaveAsync(path); var project = await StarOptProjectStore.LoadAsync(path); Assert.Single(project.OperandPickups!);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task LegacyProjectHasNoInventedPickup(int version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mce-old-{Guid.NewGuid():N}.staropt");
        try { await File.WriteAllBytesAsync(path, Container(version, null)); Assert.Null((await StarOptProjectStore.LoadAsync(path)).OperandPickups); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(6, 1, 0)]
    [InlineData(7, 1, 1)]
    [InlineData(7, 2, 0)]
    public async Task WrongVersionOrInvalidMetadataCannotLoad(int version, int target, int source)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mce-malformed-{Guid.NewGuid():N}.staropt");
        try
        {
            var row = new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, 3);
            await File.WriteAllBytesAsync(path, Container(version, [new(target, row, source, row)]));
            await Assert.ThrowsAsync<InvalidDataException>(() => StarOptProjectStore.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FullLensRowEditKeepsEverySourceValueAndUpdatesDependentConfiguration(int sourceConfiguration)
    {
        using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add(); app.MultiConfiguration.Add();
        app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.Thickness, 3), new(MultiConfigurationParameterKind.Conic, 3)], app.Events.Revision);
        app.MultiConfiguration.SetOperandPickup(1, 2, new(1, sourceConfiguration, 2, 1), app.Events.Revision);
        app.MultiConfiguration.SetOperandPickup(2, 2, new(2, sourceConfiguration, 2, .1), app.Events.Revision);
        app.MultiConfiguration.Activate(sourceConfiguration);
        var original = app.Prescription.GetSurfaces()[3];
        app.Prescription.UpdateSurface(original with { Thickness = 4, Conic = -.2, Radius = -30, Material = "N-BK7", Label = "edited source" });
        var edited = app.Prescription.GetSurfaces()[3];
        Near(4, edited.Thickness); Near(-.2, edited.Conic); Near(-30, edited.Radius);
        Assert.Equal("N-BK7", edited.Material); Assert.Equal("edited source", edited.Label);
        var rows = app.MultiConfiguration.GetOperandRows(); Near(9, rows[0].Values[2]); Near(-.3, rows[1].Values[2]);
        app.MultiConfiguration.Activate(2); Near(9, app.Prescription.GetSurfaces()[3].Thickness); Near(-.3, app.Prescription.GetSurfaces()[3].Conic);
        app.MultiConfiguration.Activate(sourceConfiguration); app.Prescription.InsertSurface(1, false);
        Assert.All(app.MultiConfiguration.GetRows(), row => Assert.Equal(9, row.SurfaceCount));
    }

    [Theory]
    [InlineData(MultiConfigurationParameterKind.Thickness, 4)]
    [InlineData(MultiConfigurationParameterKind.Curvature, -.04)]
    [InlineData(MultiConfigurationParameterKind.Conic, -.2)]
    [InlineData(MultiConfigurationParameterKind.SemiDiameter, 5)]
    public void PickupTargetsPermitUnrelatedLensEditsAndRejectDirectValueChanges(MultiConfigurationParameterKind kind, double source)
    {
        using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add();
        app.MultiConfiguration.ReplaceOperandRows([new(kind, 3)], app.Events.Revision);
        app.MultiConfiguration.SetOperandValue(1, 0, source, app.Events.Revision);
        app.MultiConfiguration.SetOperandPickup(1, 1, new(1, 0), app.Events.Revision);
        app.MultiConfiguration.Activate(1);
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[3] with { Label = "dependent", Material = "N-BK7" });
        var edited = app.Prescription.GetSurfaces()[3]; Assert.Equal("dependent", edited.Label); Assert.Equal("N-BK7", edited.Material);
        var invalid = kind switch
        {
            MultiConfigurationParameterKind.Thickness => edited with { Thickness = edited.Thickness + 1 },
            MultiConfigurationParameterKind.Curvature => edited with { Radius = edited.Radius + 1 },
            MultiConfigurationParameterKind.Conic => edited with { Conic = edited.Conic + 1 },
            _ => edited with { SemiDiameter = edited.SemiDiameter + 1 }
        };
        var revision = app.Events.Revision;
        Assert.Contains("拾取", Assert.Throws<InvalidOperationException>(() => app.Prescription.UpdateSurface(invalid)).Message);
        Assert.Equal(revision, app.Events.Revision); Assert.Equal(edited, app.Prescription.GetSurfaces()[3]);
        Near(source, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticSemiDiameterSourcesAreExplicitlyRejectedIncludingIndirectSurfacePickups(bool indirect)
    {
        var system = System(MultiConfigurationOperandKind.SemiDiameter);
        if (indirect) { system.Configurations[0].Pickups.SetSemiDiameterPickup(1, 3, 1); system.Configurations[0].Pickups.ApplyAll(); }
        Assert.Contains("自动半口径", Assert.Throws<InvalidOperationException>(() => system.SetOperandPickup(1, 1, 1, 0)).Message);
        Assert.Empty(system.OperandPickups);
        system.Configurations[0].SurfaceGroup.Items[indirect ? 1 : 3].SemiDiameterFixed = true;
        system.SetOperandPickup(1, 1, 1, 0); Assert.Single(system.OperandPickups);
    }

    [Fact]
    public void ChangingAnExistingSemiDiameterSourceToAutomaticIsRejectedAndRolledBack()
    {
        using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add();
        app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.SemiDiameter, 3)], app.Events.Revision);
        app.MultiConfiguration.SetOperandValue(1, 0, 5, app.Events.Revision);
        app.MultiConfiguration.SetOperandPickup(1, 1, new(1, 0, 2), app.Events.Revision);
        var before = app.Prescription.GetSurfaces()[3];
        Assert.Throws<InvalidOperationException>(() => app.Prescription.SetSemiDiameterSolve(3, new(SemiDiameterSolveKind.Automatic)));
        Assert.Equal(before, app.Prescription.GetSurfaces()[3]); Near(10, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
    }

    [Fact]
    public void ImageThicknessCannotBeOwnedByBothBackFocusAndMcePickup()
    {
        var system = System(MultiConfigurationOperandKind.Thickness);
        system.ReplaceOperandRows([new(MultiConfigurationOperandKind.Thickness, 3), new(MultiConfigurationOperandKind.Thickness, 7)]);
        Assert.Contains("后焦距", Assert.Throws<InvalidOperationException>(() => system.SetOperandPickup(2, 1, 1, 0)).Message);
        Assert.Empty(system.OperandPickups);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SingleSurfaceOptimizationResolvesPickupForLiveAndCandidateMerit(int configuration)
    {
        var runtime = Runtime(MultiConfigurationOperandKind.Curvature);
        runtime.ReplaceMultiConfigurationOperands([new(MultiConfigurationOperandKind.Curvature, 3), new(MultiConfigurationOperandKind.Curvature, 4)]);
        runtime.SetMultiConfigurationOperandPickup(2, configuration, 1, configuration, -.9, 0);
        runtime.ActivateMultiConfiguration(configuration);
        var result = runtime.OptimizeSurfaceRadius(runtime.Surfaces[3], "Damped Least Squares", 2);
        var context = runtime.CreateMeritConfigurationContext();
        Near(-.9 * context.OperandRows[0].Read(runtime.CurrentOptic), context.OperandRows[1].Read(runtime.CurrentOptic));
        var expected = runtime.CurrentOptic.CreateOptimizationProblem();
        foreach (var operand in MeritFunctionCatalog.CreateOperands(context, MeritFunctionCatalog.CreateDefaultRmsSpot(runtime.CurrentOptic))) expected.AddOperand(operand);
        Near(expected.SumSquared(), result.FinalMerit, 1e-9);
    }

    private static MultiConfiguration System(MultiConfigurationOperandKind kind)
    { var system = new MultiConfiguration(Optic.CreateCookeTriplet()); system.AddConfiguration(); system.ReplaceOperandRows([new(kind, 3)]); return system; }
    private static WorkbenchRuntime Runtime(MultiConfigurationOperandKind kind)
    { var runtime = new WorkbenchRuntime(Optic.CreateCookeTriplet()); runtime.AddMultiConfiguration(); runtime.ReplaceMultiConfigurationOperands([new(kind, 3)]); return runtime; }
    private static double Value(MultiConfiguration system, int configuration) => system.OperandRows[0].Read(system.Configurations[configuration]);
    private static MeritOperandDefinition Mco(int configuration, double target) => new() { Type = "MCOV", ZemaxIntegerParameters = [1, configuration], Target = target, Weight = 1 };
    private static void Near(double expected, double actual, double tolerance = 1e-9) => Assert.True(Math.Abs(expected - actual) <= tolerance, $"{expected:G17} != {actual:G17}");
    private static byte[] Container(int version, IReadOnlyList<MultiConfigurationPickup>? pickups)
    {
        var optic = Optic.CreateCookeTriplet().ToSnapshot();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            FormatVersion = version,
            Application = "Optical System Design",
            ActiveConfigurationIndex = 0,
            Configurations = new[] { optic, optic },
            OperandRows = version >= 5 ? new[] { new MultiConfigurationOperand(MultiConfigurationOperandKind.Thickness, 3) } : null,
            OperandPickups = pickups
        }, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
        using var output = new MemoryStream(); using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, true)) brotli.Write(payload);
        var compressed = output.ToArray(); var bytes = new byte[52 + compressed.Length]; "STAROPT\x1a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), StarOptProjectStore.ContainerVersion); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), payload.Length); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), compressed.Length);
        SHA256.HashData(payload).CopyTo(bytes, 20); compressed.CopyTo(bytes, 52); return bytes;
    }
}
