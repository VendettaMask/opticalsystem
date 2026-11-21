using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Phase;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxRemainingVariableTests
{
    [Theory]
    [InlineData("MM", 1d)]
    [InlineData("CM", 10d)]
    [InlineData("IN", 25.4d)]
    public async Task DualZoneParametersJointlyOptimizeThroughStableSnapshotBindings(string unit, double scale)
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(DualZone(unit));
        var keys = new[] { "r0", "b0", "p0", "s0" };
        var targets = new[] { 3.1 * scale, 1.02 * scale, 10.5, 2.2 };
        var rows = keys.Select(key => new MultiConfigurationOperand(MultiConfigurationOperandKind.ShapeCoefficient, 1, key)).ToArray();
        var runtime = Runtime(Document(imported) with { OperandRows = rows });
        runtime.ReplaceMeritFunction(targets.Select((target, index) => new OpticalSystemDesign.Core.Optimization.MeritOperandDefinition
        { Type = "MCOV", ZemaxIntegerParameters = [index + 1, 1], Target = target, Weight = 1 }).ToArray());
        Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 80).FinalMerit < 1e-8);
        for (var index = 0; index < keys.Length; index++) Near(targets[index], SurfaceShapeParameters.Read(runtime.Surfaces[1], keys[index]));
        var restored = await Reopen(runtime.CaptureDocument());
        Assert.Equal(Variables(runtime), Variables(Runtime(restored)));
        Near(targets[1], Phase(restored.ActiveOptic.SurfaceGroup.Items[1]).Inner.NormalizationRadius);
    }

    [Theory]
    [InlineData("MM", 1d)]
    [InlineData("CM", 10d)]
    [InlineData("IN", 25.4d)]
    public async Task DualZoneRadiusBoundaryAndBothPhaseFamiliesAreLiveAndSurviveEverySupportedRoundTrip(string unit, double scale)
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(DualZone(unit));
        var surface = imported.ActiveOptic.SurfaceGroup.Items[1];
        Assert.Empty(imported.ActiveOptic.ImportVariableWarnings);
        Assert.Equal(new[] { "b0", "p0", "p1", "r0", "r1", "s0", "s1" }, surface.ShapeVariables.Order(StringComparer.Ordinal));
        Near(3 * scale, SurfaceShapeParameters.Read(surface, "r0"));
        Near(scale, SurfaceShapeParameters.Read(surface, "b0"));
        Near(10, SurfaceShapeParameters.Read(surface, "p0"));
        var phase = Phase(surface); var before = phase.Gradient(.5 * scale, 0, 550);
        SurfaceShapeParameters.Write(surface, "p0", 11);
        Assert.NotEqual(before, Phase(surface).Gradient(.5 * scale, 0, 550));
        Assert.Equal(phase.Outer.Coefficients, Phase(surface).Outer.Coefficients);
        var innerSag = surface.Geometry.Sag(.5 * scale, 0); var outerSag = surface.Geometry.Sag(1.5 * scale, 0);
        SurfaceShapeParameters.Write(surface, "r0", 3.2 * scale);
        Assert.Equal(innerSag, surface.Geometry.Sag(.5 * scale, 0));
        Assert.NotEqual(outerSag, surface.Geometry.Sag(1.5 * scale, 0));
        SurfaceShapeParameters.Write(surface, "b0", 1.1 * scale);
        Near(1.1 * scale, Assert.IsType<ConcentricZoneGeometry>(surface.Geometry).BoundaryRadius);
        Near(1.1 * scale, Phase(surface).Inner.NormalizationRadius);
        var savedGeometry = surface.Geometry; var savedInteraction = surface.InteractionModel;
        Assert.Throws<ArgumentException>(() => SurfaceShapeParameters.Write(surface, "b0", 2 * scale));
        Assert.Same(savedGeometry, surface.Geometry); Assert.Same(savedInteraction, surface.InteractionModel);
        var restored = await Reopen(imported);
        Assert.Equal(Variables(Runtime(Document(imported))), Variables(Runtime(restored)));
        Assert.Equal(surface.Geometry.Sag(1.5 * scale, 0), restored.ActiveOptic.SurfaceGroup.Items[1].Geometry.Sag(1.5 * scale, 0));
        var json = JsonSerializer.Serialize(imported.ActiveOptic.ToSnapshot(), JsonOptions);
        var fromJson = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(json, JsonOptions)!);
        Assert.Equal(Variables(new WorkbenchRuntime(imported.ActiveOptic)), Variables(new WorkbenchRuntime(fromJson)));
        var zmx = OpticalFormatCatalog.Export(imported.ActiveOptic, ".zmx");
        Assert.Contains("VPAR 1", zmx); Assert.Contains("VPAR 3", zmx);
        var fromZmx = new ZemaxZmxImporter().Import(zmx);
        Assert.Equal(surface.ShapeVariables.Order(StringComparer.Ordinal), fromZmx.SurfaceGroup.Items[1].ShapeVariables.Order(StringComparer.Ordinal));
        foreach (var key in surface.ShapeVariables) Near(SurfaceShapeParameters.Read(surface, key), SurfaceShapeParameters.Read(fromZmx.SurfaceGroup.Items[1], key));
    }

    [Theory]
    [InlineData("MM", 1d)]
    [InlineData("CM", 10d)]
    [InlineData("IN", 25.4d)]
    public async Task FiniteObjectDistanceAndImageCurvatureOptimizeThroughTheActualApplicationSnapshotEvaluator(string unit, double scale)
    {
        var source = Source(unit, "TYPE STANDARD", "CURV -.01 1").Replace("DISZ INFINITY", "DISZ 20\n  VDSZ 0 0");
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Empty(imported.ActiveOptic.ImportVariableWarnings);
        var rows = new MultiConfigurationOperand[] { new(MultiConfigurationOperandKind.Thickness, 0), new(MultiConfigurationOperandKind.Curvature, 3) };
        var runtime = Runtime(Document(imported) with { OperandRows = rows });
        Assert.Contains(runtime.GetMarkedOptimizationVariables(), variable => variable.Kind == OptimizationVariableKind.ObjectDistance);
        Assert.Contains(runtime.GetMarkedOptimizationVariables(), variable => variable.SurfaceNumber == 3 && variable.Kind == OptimizationVariableKind.Radius);
        var distance = 21 * scale; var curvature = -.011 / scale;
        runtime.ReplaceMeritFunction([
            new() { Type = "MCOV", ZemaxIntegerParameters = [1, 1], Target = distance, Weight = 1 },
            new() { Type = "MCOV", ZemaxIntegerParameters = [2, 1], Target = curvature, Weight = 1e6 }
        ]);
        Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 80).FinalMerit < 1e-8);
        Near(distance, runtime.Surfaces[0].Thickness); Near(curvature, SurfaceCurvatureParameter.Read(runtime.Surfaces[3]));
        Near(runtime.Surfaces[3].Radius, Assert.IsType<StandardGeometry>(runtime.Surfaces[3].Geometry).Radius);
        var saved = await Reopen(runtime.CaptureDocument());
        Assert.Equal(Variables(runtime), Variables(Runtime(saved)));
    }

    [Theory]
    [InlineData("MM")]
    [InlineData("CM")]
    [InlineData("IN")]
    public async Task TiltedImageTangentsChangeTheExistingPlaneAndReopenWithoutDroppingFlags(string unit)
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(Source(unit, "TYPE STANDARD", "TYPE TILTSURF\n  PARM 1 .1\n  PARM 2 1.27\n  VPAR 1\n  VPAR 2"));
        Assert.Empty(imported.ActiveOptic.ImportVariableWarnings);
        var image = imported.ActiveOptic.SurfaceGroup.Items[^1];
        Assert.Equal(2, new WorkbenchRuntime(imported.ActiveOptic).GetMarkedOptimizationVariables().Count);
        var sag = image.Geometry.Sag(.2, .3);
        SurfaceShapeParameters.Write(image, "t1", 1.3);
        Assert.NotEqual(sag, image.Geometry.Sag(.2, .3));
        var restored = await Reopen(imported);
        Assert.Equal(image.Geometry.Sag(.2, .3), restored.ActiveOptic.SurfaceGroup.Items[^1].Geometry.Sag(.2, .3));
        var again = new ZemaxZmxImporter().Import(OpticalFormatCatalog.Export(imported.ActiveOptic, ".zmx"));
        Assert.Equal(image.ShapeVariables, again.SurfaceGroup.Items[^1].ShapeVariables);
        Near(1.3, SurfaceShapeParameters.Read(again.SurfaceGroup.Items[^1], "t1"));
    }

    [Theory]
    [InlineData("MM", 1d, 0)]
    [InlineData("MM", 1d, 1)]
    [InlineData("CM", 10d, 0)]
    [InlineData("CM", 10d, 1)]
    [InlineData("IN", 25.4d, 0)]
    [InlineData("IN", 25.4d, 1)]
    public async Task CoordinateVariablesRebuildAllDownstreamFramesAndMatchAFreshNativeImport(string unit, double scale, int order)
    {
        var source = CoordinateSource(unit, order, ".2", ".3");
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Empty(imported.ActiveOptic.ImportVariableWarnings);
        var surface = imported.ActiveOptic.SurfaceGroup.Items[2];
        Assert.Equal(new[] { "f0", "f1", "f2", "f3", "f4" }, surface.ShapeVariables);
        SurfaceShapeParameters.Write(surface, "f0", .25 * scale);
        SurfaceShapeParameters.Write(surface, "f2", .7);
        imported.ActiveOptic.SurfaceGroup.Renumber();
        var expected = new ZemaxZmxImporter().Import(CoordinateSource(unit, order, ".25", ".7"));
        SameFrames(expected, imported.ActiveOptic);
        var restored = await Reopen(imported);
        Assert.Equal(Variables(Runtime(Document(imported))), Variables(Runtime(restored)));
        SurfaceShapeParameters.Write(restored.ActiveOptic.SurfaceGroup.Items[2], "f2", .8);
        restored.ActiveOptic.SurfaceGroup.Renumber();
        SameFrames(new ZemaxZmxImporter().Import(CoordinateSource(unit, order, ".25", ".8")), restored.ActiveOptic);
        Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(restored.ActiveOptic, ".zmx"));
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(restored.ActiveOptic.ToSnapshot() with { SchemaVersion = 9 }));
    }

    [Fact]
    public async Task CoordinateVariableDlsUsesTheRebuiltDependencyChainAfterReopening()
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(CoordinateSource("MM", 1, ".2", ".3"));
        imported.ActiveOptic.SurfaceGroup.Items[2].ShapeVariables = ["f2"];
        var document = await Reopen(imported);
        document = document with { OperandRows = [new(MultiConfigurationOperandKind.ShapeCoefficient, 2, "f2")] };
        var runtime = Runtime(document);
        runtime.ReplaceMeritFunction([new() { Type = "MCOV", ZemaxIntegerParameters = [1, 1], Target = .7, Weight = 1 }]);
        Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 60).FinalMerit < 1e-10);
        Near(.7, SurfaceShapeParameters.Read(runtime.Surfaces[2], "f2"));
        SameFrames(new ZemaxZmxImporter().Import(CoordinateSource("MM", 1, ".2", ".7")), runtime.CurrentOptic, 1e-7);
    }

    [Fact]
    public async Task CoordinateDependenciesSurviveLensScalingAndSurfaceInsertion()
    {
        var original = new ZemaxZmxImporter().Import(CoordinateSource("MM", 1, ".2", ".3"));
        var scaled = LensScaler.Scale(original, 10, 0, original.SurfaceGroup.Items.Count - 1);
        scaled.SurfaceGroup.Renumber();
        SameFrames(new ZemaxZmxImporter().Import(CoordinateSource("CM", 1, ".2", ".3")), scaled);
        var variable = scaled.SurfaceGroup.Items[2];
        scaled.SurfaceGroup.InsertDefaultSurface(2);
        Assert.Equal(3, variable.Number);
        SurfaceShapeParameters.Write(variable, "f2", .7);
        scaled.SurfaceGroup.Renumber();
        var reopened = await Reopen(new LoadedOpticalDocument(scaled, [scaled], 0));
        SameFrames(scaled, reopened.ActiveOptic);
        Assert.Equal(variable.ShapeVariables, reopened.ActiveOptic.SurfaceGroup.Items[3].ShapeVariables);
        Near(.3, SurfaceShapeParameters.Read(original.SurfaceGroup.Items[2], "f2"));
    }

    [Fact]
    public void IncompleteCoordinateDependencyMetadataIsRejectedBeforeModelConstruction()
    {
        var optic = new ZemaxZmxImporter().Import(CoordinateSource("MM", 0, ".2", ".3"));
        var snapshot = optic.ToSnapshot();
        var surfaces = snapshot.Surfaces.ToList(); surfaces[2] = surfaces[2] with { Placement = null };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot with { Surfaces = surfaces }));
    }

    [Fact]
    public async Task ConfigurationCoordinateVariablesCannotBypassAChiefRayDependencyWhenAnotherVariableHasALiveChain()
    {
        var source = "MODE SEQ\nUNIT MM\nENPD 1\nSURF 0\nDISZ INFINITY\nSURF 1\nSTOP\nDISZ 1\nDIAM 2 1\n"
            + "SURF 2\nTYPE COORDBRK\nPARM 3 0\nVPAR 3\nDISZ 1\n"
            + "SURF 3\nTYPE COORDBRK\nPARM 2 0\nPPAR 2 -1 1 0\nDISZ 1\n"
            + "SURF 4\nTYPE COORDBRK\nPARM 1 0\nVPAR 1\nDISZ 1\nSURF 5\nDISZ 0\nDIAM 2 1\n"
            + "MNUM 2 1\nPRAM 2 1 0 1 3\nPRAM 2 2 0 1 3\n";
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Empty(imported.OperandVariables);
        Assert.All(imported.Configurations, optic => Assert.DoesNotContain("f2", optic.SurfaceGroup.Items[2].ShapeVariables));
        Assert.Contains(imported.ActiveOptic.ImportVariableWarnings, warning => warning.Contains("PRAM 3", StringComparison.Ordinal) && warning.Contains("chief-ray", StringComparison.Ordinal));
        var restored = await Reopen(imported);
        Assert.All(Runtime(restored).GetMarkedOptimizationVariables(), variable => Assert.Equal("f0", variable.Parameter));
    }

    [Fact]
    public async Task NativePrismVariablesBeforeAChiefRaySolveRemainExplicitWithTheirExactValuesAfterReopening()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples"))) root = root.Parent;
        Assert.NotNull(root);
        var path = Directory.GetFiles(Path.Combine(root.FullName, "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples"), "Apochromat_prism.zmx", SearchOption.AllDirectories).Single();
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(path);
        Assert.DoesNotContain(new WorkbenchRuntime(imported.ActiveOptic).GetMarkedOptimizationVariables(), variable => variable.Parameter == "f2");
        foreach (var (number, value) in new[] { (2, "4.278935018198"), (5, "-11.24974636752"), (11, "-7.42289984027") })
            Assert.Contains(imported.ActiveOptic.ImportVariableWarnings, warning => warning.Contains($"SURF {number} TYPE COORDBRK VPAR 3", StringComparison.Ordinal)
                && warning.Contains(value, StringComparison.Ordinal) && warning.Contains("chief-ray", StringComparison.Ordinal));
        var restored = await Reopen(imported);
        Assert.Equal(imported.ActiveOptic.ImportVariableWarnings, restored.ActiveOptic.ImportVariableWarnings);
    }

    [Fact]
    public async Task ClearUndoAndProjectReopeningCoverEveryConfigurationAndBoundarySurface()
    {
        var source = DualZone("MM").Replace("DISZ INFINITY", "DISZ 20\n  VDSZ 0 0") + "\nMNUM 2 1\n";
        var path = Path.Combine(Path.GetTempPath(), $"remaining-{Guid.NewGuid():N}.zmx");
        var saved = Path.ChangeExtension(path, ".staropt");
        try
        {
            await File.WriteAllTextAsync(path, source);
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(path);
            var original = app.Optimization.GetMarkedVariables().Select(variable => (variable.Kind, variable.Parameter, variable.FinalValue)).ToArray();
            app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll);
            Assert.Empty(app.Optimization.GetMarkedVariables());
            app.MultiConfiguration.Activate(1); Assert.Empty(app.Optimization.GetMarkedVariables());
            Assert.True(app.Documents.Undo());
            // Undo restores the activation first; the following undo restores all variable metadata.
            if (app.Optimization.GetMarkedVariables().Count == 0) Assert.True(app.Documents.Undo());
            await app.Documents.SaveAsync(saved); await app.Documents.OpenAsync(saved);
            Assert.Equal(original, app.Optimization.GetMarkedVariables().Select(variable => (variable.Kind, variable.Parameter, variable.FinalValue)));
        }
        finally { File.Delete(path); File.Delete(saved); }
    }

    private static void SameFrames(Optic expected, Optic actual, double tolerance = 1e-10)
    {
        Assert.Equal(expected.SurfaceGroup.Items.Count, actual.SurfaceGroup.Items.Count);
        foreach (var (left, right) in expected.SurfaceGroup.Items.Zip(actual.SurfaceGroup.Items))
        {
            Assert.True((left.CoordinateSystem.Origin - right.CoordinateSystem.Origin).Length < tolerance);
            foreach (var direction in new[] { new OpticalSystemDesign.Core.Backend.Vector3D(1, 0, 0), new(0, 1, 0), new(0, 0, 1) })
                Assert.True((left.CoordinateSystem.ToGlobalDirection(direction) - right.CoordinateSystem.ToGlobalDirection(direction)).Length < tolerance);
        }
    }
    private static DualZoneRadialPhaseProfile Phase(OpticalSystemDesign.Core.Domain.OpticalSurface surface) =>
        Assert.IsType<DualZoneRadialPhaseProfile>(Assert.IsType<DiffractivePhaseInteractionModel>(surface.InteractionModel).Profile);
    private static readonly JsonSerializerOptions JsonOptions = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) < Math.Max(1e-9, Math.Abs(expected) * 1e-5), $"{expected:R} != {actual:R}");
    private static string[] Variables(WorkbenchRuntime runtime) => runtime.GetMarkedOptimizationVariables().Select(variable =>
        $"{variable.ConfigurationIndex}/{variable.SurfaceNumber}/{variable.Kind}/{variable.Parameter}/{variable.FinalValue.ToString("R", CultureInfo.InvariantCulture)}").Order(StringComparer.Ordinal).ToArray();
    private static LoadedOpticalDocument Document(ZemaxZmxImportResult imported) => new(imported.ActiveOptic, imported.Configurations, imported.ActiveConfigurationIndex,
        imported.BrokenLinks, OperandRows: imported.OperandRows, OperandVariables: imported.OperandVariables, OperandPickups: imported.OperandPickups);
    private static WorkbenchRuntime Runtime(LoadedOpticalDocument document)
    { var runtime = new WorkbenchRuntime(document.ActiveOptic); runtime.ApplyLoadedDocument(document, "remaining.staropt"); return runtime; }
    private static Task<LoadedOpticalDocument> Reopen(ZemaxZmxImportResult imported) => Reopen(Document(imported));
    private static async Task<LoadedOpticalDocument> Reopen(LoadedOpticalDocument document)
    {
        var path = Path.Combine(Path.GetTempPath(), $"remaining-{Guid.NewGuid():N}.staropt");
        try { await WorkbenchRuntime.SaveDocumentAsync(document, path); return await WorkbenchRuntime.ReadDocumentAsync(path); }
        finally { File.Delete(path); }
    }
    private static string DualZone(string unit) => Source(unit, """
        TYPE BINARY_3
          CURV .2 0
          PARM 1 3
          VPAR 1
          PARM 2 0
          VPAR 2
          PARM 3 1
          VPAR 3
          PARM 4 2
          PARM 5 1
          PARM 6 1
          XDAT 1 2
          XDAT 2 0
          XDAT 3 10 1
          XDAT 4 0
          XDAT 5 2 1
          XDAT 6 0
          XDAT 7 -3 1
          XDAT 8 0
          XDAT 9 4 1
        """, "CURV 0 0");
    private static string Source(string unit, string first, string image) => $"""
        MODE SEQ
        UNIT {unit}
        ENPD 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          {first}
          DISZ 5
          DIAM 2 1
        SURF 2
          CURV -.02 0
          DISZ 10
          DIAM 2 1
        SURF 3
          {image}
          DISZ 0
          DIAM 2 1
        """;
    private static string CoordinateSource(string unit, int order, string x, string tilt) => $"""
        MODE SEQ
        UNIT {unit}
        ENPD 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          CURV .02 0
          DISZ 5
          DIAM 3 1
        SURF 2
          TYPE COORDBRK
          PARM 1 {x}
          VPAR 1
          PARM 2 -.1
          VPAR 2
          PARM 3 {tilt}
          VPAR 3
          PARM 4 .4
          VPAR 4
          PARM 5 .2
          VPAR 5
          PARM 6 {order}
          DISZ 3
        SURF 3
          CURV -.02 0
          DISZ 10
          DIAM 3 1
        SURF 4
          DISZ 0
          DIAM 3 1
        """;
}
