using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxShapeVariableImportTests
{
    [Theory]
    [InlineData("EVENASPH", "MM", 1)]
    [InlineData("EVENASPH", "CM", 10)]
    [InlineData("EVENASPH", "IN", 25.4)]
    [InlineData("ODDASPHE", "MM", 1)]
    [InlineData("ODDASPHE", "CM", 10)]
    [InlineData("ODDASPHE", "IN", 25.4)]
    public void ConicAndSparseCoefficientsKeepIdentityAndPhysicalUnits(string type, string unit, double scale)
    {
        var optic = Import(type, unit);
        var surface = optic.SurfaceGroup.Items[1];
        Assert.Equal(new[] { "conic", "c0", "c2", "c7" }, surface.ShapeVariables);
        var unmarked = new ZemaxZmxImporter().Import(Source(type, unit)
            .Replace("VCON", "").Replace("VPAR 1", "").Replace("VPAR 3", "").Replace("VPAR 8", ""));
        Assert.Equal(unmarked.SurfaceGroup.Items[1].Geometry.Sag(.3 * scale, .4 * scale), surface.Geometry.Sag(.3 * scale, .4 * scale));
        Near(-.5, surface.Conic);
        foreach (var (index, value) in new[] { (0, .0004), (1, .001), (2, -.000006), (7, 1e-16) })
        {
            var power = type == "EVENASPH" ? 2 * (index + 1) : index + 1;
            Near(value * Math.Pow(scale, 1 - power), SurfaceShapeParameters.Read(surface, $"c{index}"));
        }
        var runtime = new WorkbenchRuntime(optic);
        Assert.Equal(new[] { OptimizationVariableKind.Conic, OptimizationVariableKind.AsphereCoefficient,
            OptimizationVariableKind.AsphereCoefficient, OptimizationVariableKind.AsphereCoefficient },
            runtime.GetMarkedOptimizationVariables().Select(variable => variable.Kind));
        Assert.Equal(new[] { "conic", "c0", "c2", "c7" }, runtime.GetMarkedOptimizationVariables().Select(variable => variable.Parameter));
        Assert.Equal(surface.ShapeVariables, optic.Clone().SurfaceGroup.Items[1].ShapeVariables);
        Assert.Equal(surface.ShapeVariables, Optic.FromSnapshot(optic.ToSnapshot()).SurfaceGroup.Items[1].ShapeVariables);
    }

    [Theory]
    [InlineData("EVENASPH")]
    [InlineData("ODDASPHE")]
    public void NativeJsonAndZmxRoundTripsRetainAllVariableMarks(string type)
    {
        var optic = Import(type);
        var options = new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var json = JsonSerializer.Serialize(optic.ToSnapshot(), options);
        var restored = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(json, options)!);
        Assert.Equal(optic.SurfaceGroup.Items[1].ShapeVariables, restored.SurfaceGroup.Items[1].ShapeVariables);
        var exported = OpticalFormatCatalog.Export(restored, ".zmx");
        var reimported = new ZemaxZmxImporter().Import(exported);
        Assert.Equal(optic.SurfaceGroup.Items[1].ShapeVariables, reimported.SurfaceGroup.Items[1].ShapeVariables);
        foreach (var parameter in restored.SurfaceGroup.Items[1].ShapeVariables)
            Near(SurfaceShapeParameters.Read(restored.SurfaceGroup.Items[1], parameter),
                SurfaceShapeParameters.Read(reimported.SurfaceGroup.Items[1], parameter));
    }

    [Theory]
    [InlineData("EVENASPH", false)]
    [InlineData("ODDASPHE", true)]
    public async Task ActualFileOpenOptimizationSaveReopenClearAndUndoAgree(string type, bool unicode)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zmx-shape-{Guid.NewGuid():N}.zmx");
        var projectPath = Path.ChangeExtension(path, ".staropt");
        try
        {
            await File.WriteAllTextAsync(path, Source(type), unicode ? Encoding.Unicode : new UTF8Encoding(false));
            using var app = WorkbenchApplication.Create();
            await app.Documents.OpenAsync(path);
            Assert.Equal(new[] { "conic", "c0", "c2", "c7" }, app.Prescription.GetSurfaces()[1].ShapeVariables);
            await app.Documents.SaveAsync(projectPath);
            await app.Documents.OpenAsync(projectPath);
            Assert.Equal(4, app.Optimization.GetMarkedVariables().Count);
            // Exercise the independent snapshot evaluator used by the real optimization service.
            var document = await WorkbenchRuntime.ReadDocumentAsync(projectPath);
            foreach (var objective in Objectives()) document.ActiveOptic.MeritFunctionOperands.Add(objective);
            await WorkbenchRuntime.SaveDocumentAsync(document, projectPath);
            await app.Documents.OpenAsync(projectPath);
            var result = await app.Optimization.OptimizeVariablesAsync("Damped Least Squares", 60);
            Assert.True(result.FinalMerit < 1e-8, result.FinalMerit.ToString("G17"));
            Assert.Equal(4, result.Variables.Count);
            var targets = new[] { -.2, .0008, -.000004, 1.5e-16 };
            foreach (var (variable, target) in result.Variables.Zip(targets)) Near(target, variable.FinalValue, 1e-4);
            var optimized = app.Prescription.GetSurfaces()[1];
            Near(.001, optimized.GeometryDefinition!.Numbers["c1"]);
            await app.Documents.SaveAsync(projectPath);
            await app.Documents.OpenAsync(projectPath);
            Assert.Equal(4, app.Optimization.GetMarkedVariables().Count);
            app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll);
            Assert.Empty(app.Optimization.GetMarkedVariables());
            Assert.Empty(app.Prescription.GetSurfaces()[1].ShapeVariables!);
            Assert.True(app.Documents.Undo());
            Assert.Equal(4, app.Optimization.GetMarkedVariables().Count);
        }
        finally { File.Delete(path); File.Delete(projectPath); }
    }

    [Theory]
    [InlineData("XASPHERE")]
    [InlineData("XOSPHERE")]
    public void ExtendedAsphereCoefficientParameterIndicesDoNotShift(string type)
    {
        var source = Source(type).Replace("PARM 1 0.0004", "XDAT 1 3\n  XDAT 2 2\n  PARM 15 0.0004")
            .Replace("PARM 2 0.001", "PARM 16 0.001").Replace("PARM 3 -0.000006", "PARM 17 -0.000006")
            .Replace("PARM 8 1e-16", "").Replace("VPAR 1", "VPAR 15").Replace("VPAR 3", "VPAR 17").Replace("VPAR 8", "");
        var optic = new ZemaxZmxImporter().Import(source);
        var surface = optic.SurfaceGroup.Items[1];
        Assert.IsType<NormalizedPolynomialGeometry>(surface.Geometry);
        Assert.Equal(new[] { "conic", "c0", "c2" }, surface.ShapeVariables);
        Near(.0004, SurfaceShapeParameters.Read(surface, "c0"));
        Near(-.000006, SurfaceShapeParameters.Read(surface, "c2"));
        SurfaceShapeParameters.Write(surface, "c2", .000002);
        Near(.000002, SurfaceShapeParameters.Read(surface, "c2"));
        var reimported = new ZemaxZmxImporter().Import(OpticalFormatCatalog.Export(optic, ".zmx"));
        Assert.Equal(surface.ShapeVariables, reimported.SurfaceGroup.Items[1].ShapeVariables);
    }

    [Fact]
    public void ShapeCoefficientsPropagateOnlyThroughTheirOwnConfigurationLinks()
    {
        var first = Import(); var second = first.Clone();
        SurfaceShapeParameters.Write(second.SurfaceGroup.Items[1], "c2", .000002);
        var system = new MultiConfiguration(new[] { first, second });
        Assert.Contains(system.BrokenLinks, link => link.Property == "shape:c2");
        system.SetProperty(0, 1, "shape:c0", .0009);
        system.SetProperty(0, 1, "shape:c2", .000003);
        system.PropagateBaseLinks();
        Near(.0009, SurfaceShapeParameters.Read(system.Configurations[1].SurfaceGroup.Items[1], "c0"));
        Near(.000002, SurfaceShapeParameters.Read(system.Configurations[1].SurfaceGroup.Items[1], "c2"));
        system.SetProperty(1, 1, "shape:c0", .0007);
        system.SetProperty(0, 1, "shape:c0", .0006); system.PropagateBaseProperty(1, "shape:c0");
        Near(.0007, SurfaceShapeParameters.Read(system.Configurations[1].SurfaceGroup.Items[1], "c0"));
    }

    [Fact]
    public async Task ConfigurationShapeLinksPersistWhenVariablesAreCleared()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shape-links-{Guid.NewGuid():N}.staropt");
        try
        {
            var first = Import(); var second = first.Clone();
            SurfaceShapeParameters.Write(second.SurfaceGroup.Items[1], "c2", .000002);
            var system = new MultiConfiguration(new[] { first, second });
            foreach (var optic in system.Configurations) optic.SurfaceGroup.Items[1].ShapeVariables = Array.Empty<string>();
            await StarOptProjectStore.SaveAsync(new(system.Configurations, 1, system.BrokenLinks), path);
            var restored = await StarOptProjectStore.LoadAsync(path);
            Assert.Contains(restored.BrokenLinks!, link => link.Property == "shape:c2");
            Near(.000002, SurfaceShapeParameters.Read(restored.Configurations[1].SurfaceGroup.Items[1], "c2"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("c99")]
    [InlineData("c-1")]
    [InlineData("c01")]
    [InlineData("radius")]
    public void InvalidVariableIdentitiesAreRejectedBySnapshots(string key)
    {
        var snapshot = Import().ToSnapshot();
        var surfaces = snapshot.Surfaces.ToArray();
        surfaces[1] = surfaces[1] with { ShapeVariables = new[] { key } };
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(snapshot with { Surfaces = surfaces.ToList() }));
    }

    [Fact]
    public void OldSchemaCannotSilentlyDiscardShapeVariableMetadata()
    {
        var snapshot = Import().ToSnapshot();
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(snapshot with { SchemaVersion = 6 }));
        var surfaces = snapshot.Surfaces.Select(surface => surface with { ShapeVariables = null }).ToList();
        OpticSnapshotValidator.Validate(snapshot with { SchemaVersion = 6, Surfaces = surfaces });
    }

    [Fact]
    public void DuplicateMarkersStayUniqueAndZeroCoefficientsRemainVariables()
    {
        var optic = new ZemaxZmxImporter().Import(Source().Replace("PARM 3 -0.000006", "PARM 3 0\n  VPAR 3\n  VCON"));
        Assert.Equal(new[] { "conic", "c0", "c2", "c7" }, optic.SurfaceGroup.Items[1].ShapeVariables);
        Near(0, SurfaceShapeParameters.Read(optic.SurfaceGroup.Items[1], "c2"));
    }

    [Fact]
    public void HighestOrderCoefficientKeepsItsNonzeroScaledRange()
    {
        var surface = Import().SurfaceGroup.Items[1];
        var variable = SurfaceShapeParameters.CreateVariable(surface, "c7", () => { });
        Near(.5, variable.ScaledValue);
        variable.ScaledValue = .75;
        Near(variable.LowerBound + .75 * (variable.UpperBound - variable.LowerBound), SurfaceShapeParameters.Read(surface, "c7"));
    }

    [Fact]
    public void ChangingSurfaceTypeRemovesOnlyUnavailableCoefficientVariables()
    {
        var optic = Import();
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(50, -.5);
        Assert.Equal(new[] { "conic" }, optic.SurfaceGroup.Items[1].ShapeVariables);
        OpticSnapshotValidator.Validate(optic.ToSnapshot());
    }

    [Theory]
    [InlineData("Telescopes", "Flat-field Schmidt.zmx", 2)]
    [InlineData("Telescopes", "Schmidt-Cassegrain.zmx", 3)]
    [InlineData("Miscellaneous", "Optimization demo 1.zmx", 1)]
    public async Task BundledLensFilesExposeTheirActualConicAndAsphereVariables(string category, string name, int count)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples", category, name);
        var optic = await new ZemaxZmxImporter().ImportFileAsync(path);
        var variables = new WorkbenchRuntime(optic).GetMarkedOptimizationVariables()
            .Where(variable => variable.Kind is OptimizationVariableKind.Conic or OptimizationVariableKind.AsphereCoefficient).ToArray();
        Assert.Equal(count, variables.Length);
        if (count >= 2)
            Assert.Equal(new[] { "c1", "c2" }, variables.Where(variable => variable.Kind == OptimizationVariableKind.AsphereCoefficient).Select(variable => variable.Parameter));
    }

    private static MeritOperandDefinition[] Objectives() => [
        new() { Type = "COVA", ZemaxIntegerParameters = [1, 0], Target = -.2, Weight = 1 },
        new() { Type = "PMVA", ZemaxIntegerParameters = [1, 1], Weight = 0 },
        new() { Type = "CONS", Target = 1e3, Weight = 0 },
        new() { Type = "PROD", ZemaxIntegerParameters = [2, 3], Target = .8, Weight = 1 },
        new() { Type = "PMVA", ZemaxIntegerParameters = [1, 3], Weight = 0 },
        new() { Type = "CONS", Target = 1e5, Weight = 0 },
        new() { Type = "PROD", ZemaxIntegerParameters = [5, 6], Target = -.4, Weight = 1 },
        new() { Type = "PMVA", ZemaxIntegerParameters = [1, 8], Weight = 0 },
        new() { Type = "CONS", Target = 1e16, Weight = 0 },
        new() { Type = "PROD", ZemaxIntegerParameters = [8, 9], Target = 1.5, Weight = 1 }
    ];

    private static Optic Import(string type = "EVENASPH", string unit = "MM") => new ZemaxZmxImporter().Import(Source(type, unit));
    private static void Near(double expected, double actual, double tolerance = 1e-11) =>
        Assert.True(Math.Abs(expected - actual) <= Math.Max(1e-100, Math.Abs(expected) * tolerance), $"Expected {expected:G17}, got {actual:G17}.");

    private static string Source(string type = "EVENASPH", string unit = "MM") => $"""
        MODE SEQ
        UNIT {unit}
        ENPD 2
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          TYPE {type}
          CURV 0.02 0
          DISZ 5
          CONI -0.5
          VCON
          PARM 1 0.0004
          PARM 2 0.001
          PARM 3 -0.000006
          PARM 8 1e-16
          VPAR 1
          VPAR 3
          VPAR 8
          DIAM 2 1
        SURF 2
          CURV -0.02 0
          DISZ 10
          DIAM 2 1
        SURF 3
          DISZ 0
          DIAM 2 1
        """;
}
