using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class ZemaxVariableAuditTests
{
    public static IEnumerable<object[]> CoefficientInputs()
    {
        var inputs = new (string Type, string Body, string[] Keys)[]
        {
            ("EVENASPH", "PARM 1 0\nPARM 8 1e-16\nVPAR 1\nVPAR 8", ["c0", "c7"]),
            ("ODDASPHE", "PARM 1 0\nPARM 8 1e-16\nVPAR 1\nVPAR 8", ["c0", "c7"]),
            ("BINARY_2", "PARM 0 1\nPARM 1 0\nPARM 8 1e-16\nVPAR 1\nVPAR 8\nXDAT 1 0\nXDAT 2 2", ["c0", "c7"]),
            ("XASPHERE", "XDAT 1 2\nXDAT 2 2\nXDAT 3 .0004 1\nXDAT 4 0 1", ["c0", "c1"]),
            ("XOSPHERE", "XDAT 1 2\nXDAT 2 2\nXDAT 3 .0004 1\nXDAT 4 0 1", ["c0", "c1"]),
            ("XPOLYNOM", "XDAT 1 2\nXDAT 2 2\nXDAT 3 .0004 1\nXDAT 4 0 1", ["c0", "c1"]),
            ("FZERNSAG", "PARM 1 .0004\nVPAR 1\nXDAT 1 2\nXDAT 2 2\nXDAT 3 .001 1\nXDAT 4 -.002 1", ["a0", "z0", "z1"]),
            ("TOROIDAL", "PARM 1 70\nPARM 2 .0004\nPARM 8 0\nVPAR 2\nVPAR 8", ["a0", "a6"]),
            ("QED_TYPE", "XDAT 1 0\nXDAT 2 2\nXDAT 3 2\nXDAT 4 .0004 1\nXDAT 5 0 1", ["q0", "q1"]),
            ("CHEBYSHV", "PARM 1 1\nPARM 2 1\nPARM 3 2\nPARM 4 2\nXDAT 1 0\nXDAT 2 -.00025 1\nXDAT 3 .0001 1", ["c1", "c2"]),
            ("BINARY_3", "PARM 1 70\nPARM 2 -.2\nPARM 3 1\nPARM 4 2\nPARM 5 1\nPARM 6 1\nXDAT 1 1\nXDAT 2 .0003 1\nXDAT 3 0\nXDAT 4 -.0004 1\nXDAT 5 0", ["i0", "o0"]),
            ("GRID_SAG", "PARM 1 .0004\nPARM 8 0\nVPAR 1\nVPAR 8\nGDAT 5 5 1 1 0 0 0 0 0 1\n"
                + string.Join("\n", Enumerable.Range(0, 25).Select(index => $"GARR {index} 0 0 0 0 0")), ["a0", "a7"])
        };
        foreach (var input in inputs)
            foreach (var unit in input.Type == "GRID_SAG" ? new[] { "MM" } : new[] { "MM", "CM", "IN" }) yield return [input.Type, input.Body, input.Keys, unit];
    }

    [Theory]
    [MemberData(nameof(CoefficientInputs))]
    public async Task EverySupportedCoefficientFamilyKeepsFlagsValuesAndNativeEncoding(string type, string body, string[] keys, string unit)
    {
        var source = Source(type, body, unit);
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        var optic = imported.ActiveOptic;
        Assert.Empty(optic.ImportVariableWarnings);
        Assert.Equal(keys.Order(StringComparer.Ordinal), optic.SurfaceGroup.Items[1].ShapeVariables.Order(StringComparer.Ordinal));
        var withoutFlags = Regex.Replace(source, @"(?m)^[ \t]*VPAR[ \t]+\d+[^\r\n]*", "");
        withoutFlags = Regex.Replace(withoutFlags, @"(?m)^([ \t]*XDAT[ \t]+\d+[ \t]+\S+)[ \t]+1(?:[ \t]+[^\r\n]*)?\r?$", "$1 0");
        var fixedOptic = new ZemaxZmxImporter().Import(withoutFlags);
        Assert.Equal(fixedOptic.SurfaceGroup.Items[1].Geometry.Sag(.2, .3), optic.SurfaceGroup.Items[1].Geometry.Sag(.2, .3));
        var restored = await RoundTrip(imported);
        Assert.Equal(Variables(new WorkbenchRuntime(optic)), Variables(Runtime(restored)));
        var text = OpticalFormatCatalog.Export(optic, ".zmx");
        var again = new ZemaxZmxImporter().Import(text);
        Assert.Equal(keys.Order(StringComparer.Ordinal), again.SurfaceGroup.Items[1].ShapeVariables.Order(StringComparer.Ordinal));
        foreach (var key in keys)
        {
            Assert.Equal(SurfaceShapeParameters.Read(optic.SurfaceGroup.Items[1], key), SurfaceShapeParameters.Read(again.SurfaceGroup.Items[1], key));
            var field = SurfaceShapeParameters.ZemaxVariableField(optic.SurfaceGroup.Items[1], key);
            if (field.ExtraData) Assert.Matches($@"(?m)^\s*XDAT {field.Number} \S+ 1(?:\s|$)", text);
        }
    }

    [Theory]
    [InlineData("FZERNSAG", true)]
    [InlineData("CHEBYSHV", false)]
    public async Task ImportedBaseVariablesUpdateTheActualGeometryAndKeepCoefficientValues(string type, bool supportsConic)
    {
        var body = type == "FZERNSAG" ? "PARM 1 .0004\nXDAT 1 1\nXDAT 2 2\nXDAT 3 .001"
            : "PARM 1 1\nPARM 2 1\nPARM 3 2\nPARM 4 2\nXDAT 1 .0002\nXDAT 2 -.00025\nXDAT 3 .0001";
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(Source(type, body + "\nVCON").Replace("CURV .02 0", "CURV .02 1"));
        var surface = imported.ActiveOptic.SurfaceGroup.Items[1];
        Assert.True(surface.RadiusVariable);
        Assert.Equal(supportsConic, surface.ShapeVariables.Contains("conic"));
        Assert.Equal(supportsConic, imported.ActiveOptic.ImportVariableWarnings.Count == 0);
        var names = SurfaceShapeParameters.CoefficientNames(surface);
        var coefficients = names.Select(key => SurfaceShapeParameters.Read(surface, key)).ToArray();
        var originalSag = surface.Geometry.Sag(.3, .4);
        surface.Radius = 40;
        Assert.NotEqual(originalSag, surface.Geometry.Sag(.3, .4));
        if (supportsConic)
        {
            var sag = surface.Geometry.Sag(.3, .4);
            SurfaceShapeParameters.Write(surface, "conic", -.8);
            Assert.NotEqual(sag, surface.Geometry.Sag(.3, .4));
            Assert.Equal(-.8, Assert.IsType<FringeSagGeometry>(surface.Geometry).Base.Base.Conic);
        }
        Assert.Equal(coefficients, names.Select(key => SurfaceShapeParameters.Read(surface, key)));
        var restored = await RoundTrip(imported);
        Assert.Equal(surface.Geometry.Sag(.3, .4), restored.ActiveOptic.SurfaceGroup.Items[1].Geometry.Sag(.3, .4));
        Assert.Equal(Variables(Runtime(Document(imported))), Variables(Runtime(restored)));
    }

    [Fact]
    public async Task UnavailableBaseVariablesAreDiagnosedForBothSurfaceAndConfigurationMarkers()
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(Source("TILTSURF", "PARM 1 .01\nPARM 2 .02\nVCON")
            .Replace("CURV .02 0", "CURV .02 1") + "\nMNUM 2 1\nCONN 1 1 0 1\nCRVT 1 2 0 1\n");
        Assert.All(imported.Configurations, optic => Assert.False(optic.SurfaceGroup.Items[1].RadiusVariable));
        Assert.Empty(imported.OperandVariables);
        var warnings = imported.Configurations.SelectMany(optic => optic.ImportVariableWarnings).ToArray();
        Assert.Contains(warnings, warning => warning.Contains("CONN", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("CRVT", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.Contains("CURV", StringComparison.Ordinal));
        var restored = await RoundTrip(imported);
        Assert.Empty(Runtime(restored).GetMarkedOptimizationVariables());
        Assert.Equal(warnings, restored.Configurations.SelectMany(optic => optic.ImportVariableWarnings));
    }

    [Theory]
    [InlineData("MM")]
    [InlineData("CM")]
    [InlineData("IN")]
    public async Task AllFourConfigurationVariableKindsRemainIndependentWhenFixedValuesEqualTheBase(string unit)
    {
        var source = Source("STANDARD", "VCON\nVDSZ 0 0", unit).Replace("CURV .02 0", "CURV .02 1") + """

            MNUM 2 1
            THIC 1 1 5 1
            THIC 1 2 5 0
            CRVT 1 1 .02 0
            CRVT 1 2 .02 1
            CONN 1 1 -.3 1
            CONN 1 2 -.3 0
            SDIA 1 1 2 0
            SDIA 1 2 2 1
            """;
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        var restored = await RoundTrip(imported);
        var runtime = Runtime(restored);
        Assert.Equal(new[] { OptimizationVariableKind.Thickness, OptimizationVariableKind.Curvature, OptimizationVariableKind.Conic, OptimizationVariableKind.SemiDiameter },
            runtime.GetMarkedOptimizationVariables().Select(variable => variable.Kind));
        var initialSecondThickness = restored.Configurations[1].SurfaceGroup.Items[1].Thickness;
        var initialSecondConic = restored.Configurations[1].SurfaceGroup.Items[1].Conic;
        runtime.ReplaceMeritFunction([
            new() { Type = "MCOV", ZemaxIntegerParameters = [1, 1], Target = initialSecondThickness + 1, Weight = 1 },
            new() { Type = "MCOV", ZemaxIntegerParameters = [3, 1], Target = -.1, Weight = 1 }
        ]);
        runtime.SetMultiConfigurationOperandVariable(2, 1, false); runtime.SetMultiConfigurationOperandVariable(4, 1, false);
        Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 50).FinalMerit < 1e-8);
        var context = runtime.CreateMeritConfigurationContext();
        Assert.Equal(initialSecondThickness, context.Configurations[1].SurfaceGroup.Items[1].Thickness);
        Assert.Equal(initialSecondConic, context.Configurations[1].SurfaceGroup.Items[1].Conic);
    }

    [Theory]
    [InlineData("PRAM", 1, "EVENASPH", "c0")]
    [InlineData("PAR1", 1, "EVENASPH", "c0")]
    [InlineData("EDVA", 3, "XASPHERE", "c0")]
    [InlineData("PRAM", 15, "XASPHERE", "c0")]
    [InlineData("PRAM", 2, "GRINSUR1", "n0")]
    [InlineData("PRAM", 3, "GRINSUR1", "nr2")]
    public async Task ParameterCellsOverrideGlobalMarksAndKeepTheInactiveConfigurationIdentity(string command, int number, string type, string key)
    {
        var body = type switch
        {
            "EVENASPH" => "PARM 1 .0004\nVPAR 1",
            "XASPHERE" => "XDAT 1 1\nXDAT 2 2\nXDAT 3 .0004 1",
            _ => $"PARM 1 .1\nPARM 2 1.6\nPARM 3 -.001\nVPAR {number}"
        };
        var value = type == "GRINSUR1" ? number == 2 ? "1.6" : "-.001" : ".0004";
        var source = Source(type, body) + $"\nMNUM 2 1\n{command} 1 1 {value} 0 {number} 0\n{command} 1 2 {value} 1 {number} 0";
        var restored = await RoundTrip(new ZemaxZmxImporter().ImportConfigurationSet(source));
        var runtime = Runtime(restored);
        var variable = Assert.Single(runtime.GetMarkedOptimizationVariables());
        Assert.Equal(1, variable.ConfigurationIndex); Assert.Equal(key, variable.Parameter);
        Assert.Single(runtime.GetMultiConfigurationOperands());
        var initial = variable.InitialValue;
        var target = initial + (key == "n0" ? .02 : key == "nr2" ? .0002 : .0001);
        var factor = key == "n0" ? 1 : 1000;
        runtime.ReplaceMeritFunction([
            new() { Type = "MCOV", ZemaxIntegerParameters = [1, 2], Weight = 0 },
            new() { Type = "CONS", Target = factor, Weight = 0 },
            new() { Type = "PROD", ZemaxIntegerParameters = [1, 2], Target = target * factor, Weight = 1 }
        ]);
        Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 60).FinalMerit < 1e-8);
        Assert.Equal(initial, runtime.CreateMeritConfigurationContext().OperandRows[0].Read(runtime.CreateMeritConfigurationContext().Configurations[0]));
        Near(target, Assert.Single(runtime.GetMarkedOptimizationVariables()).FinalValue);
    }

    [Theory]
    [InlineData("MM")]
    [InlineData("CM")]
    [InlineData("IN")]
    public async Task GradientCoefficientVariablesSurviveNativeJsonAndProjectReopening(string unit)
    {
        var source = Source("GRINSUR1", "PARM 1 .1\nPARM 2 1.6\nPARM 3 -.001\nVPAR 2\nVPAR 3", unit);
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Empty(imported.ActiveOptic.ImportVariableWarnings);
        var material = Assert.IsType<GradientIndexMaterial>(imported.ActiveOptic.SurfaceGroup.Items[1].MaterialAfter);
        Assert.Equal(new[] { "n0", "nr2" }, material.Variables.Keys.Order(StringComparer.Ordinal));
        var restored = await RoundTrip(imported);
        Assert.Equal(Variables(new WorkbenchRuntime(imported.ActiveOptic)), Variables(Runtime(restored)));
        var json = JsonSerializer.Serialize(imported.ActiveOptic.ToSnapshot(), JsonOptions);
        Assert.Equal(Variables(new WorkbenchRuntime(imported.ActiveOptic)), Variables(new WorkbenchRuntime(Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(json, JsonOptions)!))));
        Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(imported.ActiveOptic, ".zmx"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoefficientPickupDependenciesRemainLiveAfterProjectReopening(bool gradient)
    {
        var type = gradient ? "GRINSUR1" : "EVENASPH";
        var parameter = gradient ? 3 : 1;
        var body = gradient ? "PARM 1 .1\nPARM 2 1.6\nPARM 3 -.001\nVPAR 3" : "PARM 1 .0004\nVPAR 1";
        var second = gradient ? "TYPE GRINSUR1\nPARM 1 .1\nPARM 2 1.6\nPARM 3 -.002" : "TYPE EVENASPH\nPARM 1 .0008";
        var source = Source(type, body).Replace("SURF 2\n", $"SURF 2\n{second}\nPPAR {parameter} 1 2 0 0\n");
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Empty(imported.ActiveOptic.ImportVariableWarnings);
        Assert.Single(imported.OperandPickups);
        var runtime = Runtime(await RoundTrip(imported));
        Assert.Single(runtime.GetMarkedOptimizationVariables());
        runtime.ReplaceMeritFunction([
            new() { Type = "MCOV", ZemaxIntegerParameters = [2, 1], Weight = 0 },
            new() { Type = "CONS", Target = 1000, Weight = 0 },
            new() { Type = "PROD", ZemaxIntegerParameters = [1, 2], Target = gradient ? -1.4 : 1.2, Weight = 1 }
        ]);
        Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 60).FinalMerit < 1e-8);
        var key = gradient ? "nr2" : "c0";
        double Read(int index) => gradient ? GradientIndexParameters.Read(runtime.Surfaces[index], key) : SurfaceShapeParameters.Read(runtime.Surfaces[index], key);
        Near(gradient ? -.0007 : .0006, Read(1)); Near(2 * Read(1), Read(2));
        Assert.Equal(0, runtime.Surfaces[^1].Thickness);
    }

    [Fact]
    public async Task UnsupportedVariableRecordsRemainVisibleAndPersistWithoutInventedBindings()
    {
        var source = Source("TILTSURF", "PARM 1 .01\nPARM 2 .02\nVPAR 3");
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Contains(imported.ActiveOptic.ImportVariableWarnings, warning => warning.Contains("VPAR 3", StringComparison.Ordinal));
        var restored = await RoundTrip(imported);
        Assert.Equal(imported.ActiveOptic.ImportVariableWarnings, restored.ActiveOptic.ImportVariableWarnings);
        Assert.Empty(Runtime(restored).GetMarkedOptimizationVariables());
        Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(restored.ActiveOptic, ".zmx"));
    }

    [Fact]
    public void ModelGlassSolveAndItsPickupUseTheSpecifiedDispersion()
    {
        var source = Source("STANDARD", "GLAS ___BLANK 1 0 1.61 57 .002 1 0 1 0 0")
            .Replace("SURF 2", "SURF 2\nGLAS unused 2 1");
        var imported = OpticalFormatCatalog.Import(source, ".zmx");
        var model = Assert.IsType<ModelGlassMaterial>(imported.SurfaceGroup.Items[1].MaterialAfter);
        var pickup = Assert.IsType<ModelGlassMaterial>(imported.SurfaceGroup.Items[2].MaterialAfter);
        Assert.Equal(new[] { "nd", "dpgf" }, model.Variables);
        Assert.Empty(pickup.Variables);
        Assert.Equal(model.RefractiveIndex(480), pickup.RefractiveIndex(480));
        var exported = OpticalFormatCatalog.Export(imported, ".zmx");
        Assert.Contains("GLAS ___BLANK 1 0", exported);
        var restored = OpticalFormatCatalog.Import(exported, ".zmx");
        var copy = Assert.IsType<ModelGlassMaterial>(restored.SurfaceGroup.Items[1].MaterialAfter);
        Assert.Equal(model.Variables, copy.Variables);
        Assert.Equal(model.RefractiveIndex(650), copy.RefractiveIndex(650));
    }

    [Fact]
    public async Task SubstituteGlassAndUnsupportedConfigurationVariablesKeepExplicitDiagnostics()
    {
        var source = Source("STANDARD", "GLAS N-BK7 3 0 1.6 60 .002 1 1 1 0 0")
            + "\nMNUM 1 1\nMIND 1 1 1.6 1 0 0 1 1 1 0 0\nAPER 0 1 2 1 0 0 1 2 1 0 0";
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(source);
        Assert.Contains(imported.ActiveOptic.ImportVariableWarnings, warning => warning.Contains("GLAS 0:", StringComparison.Ordinal));
        Assert.Contains(imported.ActiveOptic.ImportVariableWarnings, warning => warning.Contains("MIND 0:", StringComparison.Ordinal));
        Assert.Contains(imported.ActiveOptic.ImportVariableWarnings, warning => warning.Contains("APER 0:", StringComparison.Ordinal));
        var restored = await RoundTrip(imported);
        Assert.Equal(imported.ActiveOptic.ImportVariableWarnings, restored.ActiveOptic.ImportVariableWarnings);
        Assert.Empty(Runtime(restored).GetMarkedOptimizationVariables());
    }

    [Fact]
    public async Task UnmappedPickupBlocksOnlyItsActualSourceVariableAfterReopening()
    {
        var source = Source("STANDARD", "").Replace("CURV .02 0", "CURV .02 1")
            .Replace("SURF 2\n", "SURF 2\nTYPE PARAXIAL\nPARM 1 100\nPARM 2 1\nPPAR 1 1 1 0 1\n");
        var runtime = Runtime(await RoundTrip(new ZemaxZmxImporter().ImportConfigurationSet(source)));
        Assert.Contains("radius", runtime.Surfaces[1].BlockedVariableProperties);
        Assert.Throws<NotSupportedException>(() => runtime.OptimizeMarkedVariables("Damped Least Squares", 5));
        Assert.Throws<NotSupportedException>(() => runtime.OptimizeSurfaceRadius(runtime.Surfaces[1], "Damped Least Squares", 5));
        runtime.SetRadiusSolve(1, new(RadiusSolveKind.Fixed)); runtime.SetThicknessSolve(1, new(ThicknessSolveKind.Variable));
        runtime.ReplaceMeritFunction([new() { Type = "TTHI", ZemaxIntegerParameters = [1, 1], Target = 6, Weight = 1 }]);
        Assert.True(runtime.OptimizeMarkedVariables("Damped Least Squares", 30).FinalMerit < 1e-8);
    }

    [Fact]
    public async Task ExistingCoatingVariableFamiliesKeepTheirFlagsThroughNativeProjectReopening()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([
            new(new ConstantIndexMaterial("audit-film", 1.8), 100, new(MultiplierVariable: true, IndexVariable: true, ExtinctionVariable: true))]);
        var imported = new ZemaxZmxImportResult(optic, new[] { optic }, 0);
        var restored = await RoundTrip(imported);
        Assert.Equal(new[] { OptimizationVariableKind.CoatingMultiplier, OptimizationVariableKind.CoatingIndexOffset, OptimizationVariableKind.CoatingExtinctionOffset },
            Runtime(restored).GetMarkedOptimizationVariables().Select(variable => variable.Kind));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationParameterRowsRetainIdentityAcrossReorderingClearingUndoAndReopening(bool gradient)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zmx-audit-api-{Guid.NewGuid():N}.zmx");
        var project = Path.ChangeExtension(path, ".staropt");
        try
        {
            var body = gradient ? "PARM 1 .1\nPARM 2 1.6\nPARM 3 -.001" : "PARM 1 .0004\nPARM 2 .0002";
            var numbers = gradient ? new[] { 2, 3 } : new[] { 1, 2 };
            var values = gradient ? new[] { "1.6", "-.001" } : new[] { ".0004", ".0002" };
            var source = Source(gradient ? "GRINSUR1" : "EVENASPH", body) + "\nMNUM 2 1";
            for (var row = 0; row < numbers.Length; row++)
                source += $"\nPRAM 1 1 {values[row]} 0 {numbers[row]} 0\nPRAM 1 2 {values[row]} 1 {numbers[row]} 0";
            await File.WriteAllTextAsync(path, source);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            var rows = app.MultiConfiguration.GetOperandRows();
            Assert.Equal(2, rows.Count); Assert.All(rows, row => Assert.Equal(new[] { false, true }, row.Variables));
            var parameters = rows.Select(row => row.Parameter).ToArray();
            app.MultiConfiguration.ReplaceOperandRows(rows.Reverse().Select(row => new MultiConfigurationOperandBindingDto(row.Kind, row.SurfaceNumber, row.Parameter)).ToArray(), app.Events.Revision);
            Assert.Equal(parameters.Reverse(), app.MultiConfiguration.GetOperandRows().Select(row => row.Parameter));
            app.MultiConfiguration.Activate(1);
            Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
            app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll);
            Assert.Empty(app.Optimization.GetMarkedVariables());
            Assert.True(app.Documents.Undo()); Assert.Equal(2, app.Optimization.GetMarkedVariables().Count);
            await app.Documents.SaveAsync(project); await app.Documents.OpenAsync(project);
            Assert.Equal(parameters.Reverse(), app.MultiConfiguration.GetOperandRows().Select(row => row.Parameter));
            Assert.Equal(parameters.Order(StringComparer.Ordinal), app.Optimization.GetMarkedVariables().Select(variable => variable.Parameter).Order(StringComparer.Ordinal));
        }
        finally { File.Delete(path); File.Delete(project); }
    }

    [Fact]
    public async Task EntireBundledSequentialCorpusAccountsForVariableMarkersAndKeepsSavedState()
    {
        var root = RepositoryRoot();
        var corpus = Path.Combine(root, "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples");
        var files = Directory.GetFiles(corpus, "*", SearchOption.AllDirectories).Where(path => Path.GetExtension(path).Equals(".zmx", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(95, files.Length);
        var results = new List<object>(); var failures = new List<string>();
        foreach (var path in files)
        {
            try
            {
                var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(path);
                var before = Runtime(Document(imported));
                var after = Runtime(await RoundTrip(imported));
                Assert.Equal(Variables(before), Variables(after));
                Assert.Equal(before.CaptureDocument().Configurations.SelectMany(optic => optic.ImportVariableWarnings),
                    after.CaptureDocument().Configurations.SelectMany(optic => optic.ImportVariableWarnings));
                var markers = ReadMarkers(await File.ReadAllTextAsync(path));
                var coverage = markers.Select(marker => AccountForMarker(marker, imported)).ToArray();
                Assert.DoesNotContain(coverage, item => item.Status == "Missing");
                results.Add(new
                {
                    File = Path.GetRelativePath(corpus, path),
                    Configurations = imported.Configurations.Count,
                    Variables = Variables(after),
                    Coverage = coverage,
                    Diagnostics = imported.Configurations.SelectMany(optic => optic.ImportVariableWarnings).Distinct().ToArray()
                });
            }
            catch (Exception exception)
            {
                failures.Add($"{Path.GetFileName(path)}: {exception.Message}");
                results.Add(new { File = Path.GetRelativePath(corpus, path), Error = exception.ToString() });
            }
        }
        var receipt = Path.Combine(root, "validation", "rechecks", "2026-11-21", "zemax-variable-import", "extended-corpus.json");
        await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new { Scope = "95 bundled sequential files: variable metadata and native project reopening; no numerical precision certification", Total = files.Length, Passed = results.Count - failures.Count, Failures = failures, Results = results }, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private sealed record Marker(int Surface, string Type, string Field, int Number, int Configuration = -1);
    private sealed record Coverage(Marker Marker, string Status);

    private static List<Marker> ReadMarkers(string text)
    {
        var result = new List<Marker>(); var types = new Dictionary<int, string>(); var surface = -1;
        var lines = text.Split('\n').Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToArray();
        foreach (var tokens in lines)
        {
            if (tokens.Length == 0) continue;
            if (tokens[0] == "SURF") { surface = int.Parse(tokens[1], CultureInfo.InvariantCulture); types[surface] = "STANDARD"; }
            if (tokens[0] == "TYPE" && surface >= 0) types[surface] = tokens[1];
            if (tokens[0] is "VCON" or "VDSZ") result.Add(new(surface, types[surface], tokens[0], 0));
            if (tokens[0] == "VPAR") result.Add(new(surface, types[surface], "VPAR", int.Parse(tokens[1], CultureInfo.InvariantCulture)));
            if (tokens[0] == "CURV" && tokens.Length > 2 && tokens[2] == "1") result.Add(new(surface, types[surface], "CURV", 0));
            if (tokens[0] == "XDAT" && tokens.Length > 3 && tokens[3] == "1") result.Add(new(surface, types[surface], "XDAT", int.Parse(tokens[1], CultureInfo.InvariantCulture)));
            if (tokens[0] == "GLAS" && tokens.Length > 2 && tokens[2] == "3")
                foreach (var field in new[] { 7, 8, 9 })
                    if (tokens.Length > field && tokens[field] == "1") result.Add(new(surface, types[surface], "GLAS", field));
            if (tokens[0] == "GLAS" && tokens.Length > 2 && tokens[2] is not ("0" or "2" or "3")) result.Add(new(surface, types[surface], "GLAS", 0));
            if (tokens[0] is "THIC" or "CRVT" or "CONN" or "SDIA" or "PRAM" or "EDVA" && tokens.Length > 4 && tokens[4] == "1")
            {
                var number = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                result.Add(new(number, types[number], tokens[0], tokens[0] is "PRAM" or "EDVA" ? int.Parse(tokens[5], CultureInfo.InvariantCulture) : 0,
                    int.Parse(tokens[2], CultureInfo.InvariantCulture) - 1));
            }
        }
        return result;
    }

    private static Coverage AccountForMarker(Marker marker, ZemaxZmxImportResult imported)
    {
        var field = marker.Field;
        string? key = field == "VCON" || field == "CONN" ? "conic" : field is "VDSZ" or "THIC" ? "thickness" : field is "CURV" or "CRVT" ? "radius" : field == "SDIA" ? "semiDiameter" : null;
        var parameterField = field == "PRAM" ? "VPAR" : field == "EDVA" ? "XDAT" : field;
        if (parameterField is "VPAR" or "XDAT")
        {
            key = (marker.Type, parameterField, marker.Number) switch
            {
                ("EVENASPH" or "ODDASPHE" or "BINARY_2", "VPAR", >= 1 and <= 8) => $"c{marker.Number - 1}",
                ("FZERNSAG" or "GRID_SAG", "VPAR", >= 1 and <= 8) => $"a{marker.Number - 1}",
                ("TOROIDAL", "VPAR", >= 2 and <= 8) => $"a{marker.Number - 2}",
                ("CHEBYSHV", "XDAT", >= 1) => $"c{marker.Number - 1}",
                ("FZERNSAG", "XDAT", >= 3) => $"z{marker.Number - 3}",
                ("XASPHERE" or "XOSPHERE" or "XPOLYNOM", "XDAT", >= 3) => $"c{marker.Number - 3}",
                ("QED_TYPE", "XDAT", >= 4) => $"q{marker.Number - 4}",
                ("GRINSUR1", "VPAR", 2) => "n0",
                ("GRINSUR1", "VPAR", 3) => "nr2",
                ("GRINSUR1", "VPAR", 4) => "nr1",
                ("BINARY_3", "XDAT", >= 2) when (marker.Number - 2) % 4 == 0 => $"i{(marker.Number - 2) / 4}",
                ("BINARY_3", "XDAT", >= 2) when (marker.Number - 2) % 4 == 2 => $"o{(marker.Number - 2) / 4}",
                ("BINARY_3", "XDAT", >= 3) when (marker.Number - 3) % 4 == 0 => $"p{(marker.Number - 3) / 4}",
                ("BINARY_3", "XDAT", >= 3) when (marker.Number - 3) % 4 == 2 => $"s{(marker.Number - 3) / 4}",
                ("BINARY_3", "VPAR", 1) => "r0",
                ("BINARY_3", "VPAR", 2) => "r1",
                ("BINARY_3", "VPAR", 3) => "b0",
                ("TILTSURF", "VPAR", 1 or 2) => $"t{marker.Number - 1}",
                ("COORDBRK", "VPAR", >= 1 and <= 5) => $"f{marker.Number - 1}",
                _ => null
            };
        }
        var configurations = marker.Configuration < 0 ? Enumerable.Range(0, imported.Configurations.Count) : new[] { marker.Configuration };
        foreach (var configuration in configurations)
        {
            var index = imported.SourceSurfaceNumbers[configuration].ToList().IndexOf(marker.Surface);
            if (index < 0) continue;
            var surface = imported.Configurations[configuration].SurfaceGroup.Items[index];
            if (marker.Configuration >= 0 && imported.OperandVariables.Any(variable => variable.ConfigurationIndex == configuration && variable.Operand.SurfaceNumber == index
                && key is not null && (variable.Operand.Property == key || variable.Operand.Parameter == key))) return new(marker, "Synchronized");
            if ((key == "radius" && index > 0 && surface.RadiusVariable || key == "thickness" && index < imported.Configurations[configuration].SurfaceGroup.Items.Count - 1
                    && double.IsFinite(surface.Thickness) && surface.ThicknessVariable || key is not null && surface.ShapeVariables.Contains(key)
                    || key is not null && surface.MaterialAfter is GradientIndexMaterial gradient && gradient.Variables.ContainsKey(key))) return new(marker, "Synchronized");
        }
        var diagnostic = $"SURF {marker.Surface} TYPE {marker.Type} {marker.Field} {marker.Number}:";
        if (imported.Configurations.SelectMany(optic => optic.ImportVariableWarnings).Any(warning => warning.Contains(diagnostic, StringComparison.Ordinal))) return new(marker, "UnsupportedRecorded");
        if (imported.Configurations.SelectMany(optic => optic.ImportVariableWarnings).Any(warning => warning.Contains($"SURF {marker.Surface} TYPE {marker.Type} SURF {marker.Surface}:", StringComparison.Ordinal))) return new(marker, "UnsupportedRecorded");
        if (marker.Configuration < 0 && key is not null && imported.OperandRows.Any(row =>
                row.SurfaceNumber >= 0 && row.SurfaceNumber < imported.SourceSurfaceNumbers[0].Count
                && imported.SourceSurfaceNumbers[0][row.SurfaceNumber] == marker.Surface
                && (row.Parameter == key || row.Property == key)))
            return new(marker, "OverriddenByConfiguration");
        return new(marker, "Missing");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "OpticalSystemDesign.App", "Assets", "SequentialExamples"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository source root not found.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static string[] Variables(WorkbenchRuntime runtime) => runtime.GetMarkedOptimizationVariables().Select(variable =>
        $"{variable.ConfigurationIndex}/{variable.SurfaceNumber}/{variable.Kind}/{variable.Layer}/{variable.Parameter}={variable.FinalValue.ToString("R", CultureInfo.InvariantCulture)}").Order(StringComparer.Ordinal).ToArray();
    private static LoadedOpticalDocument Document(ZemaxZmxImportResult imported) => new(imported.ActiveOptic, imported.Configurations, imported.ActiveConfigurationIndex,
        imported.BrokenLinks, OperandRows: imported.OperandRows, OperandVariables: imported.OperandVariables, OperandPickups: imported.OperandPickups);
    private static WorkbenchRuntime Runtime(LoadedOpticalDocument document)
    { var runtime = new WorkbenchRuntime(document.ActiveOptic); runtime.ApplyLoadedDocument(document, "audit.staropt"); return runtime; }
    private static async Task<LoadedOpticalDocument> RoundTrip(ZemaxZmxImportResult imported)
    {
        var path = Path.Combine(Path.GetTempPath(), $"zmx-audit-{Guid.NewGuid():N}.staropt");
        try { await WorkbenchRuntime.SaveDocumentAsync(Document(imported), path); return await WorkbenchRuntime.ReadDocumentAsync(path); }
        finally { File.Delete(path); }
    }
    private static void Near(double expected, double actual) => Assert.True(Math.Abs(expected - actual) <= Math.Max(1e-12, Math.Abs(expected) * 1e-5), $"{expected:G17} != {actual:G17}");
    private static string Source(string type, string body, string unit = "MM") => $"""
        MODE SEQ
        UNIT {unit}
        ENPD 2
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          TYPE {type}
          CURV .02 0
          DISZ 5
          CONI -.3
        {body}
          DIAM 2 1
        SURF 2
          CURV -.02 0
          DISZ 10
          DIAM 2 1
        SURF 3
          DISZ 0
          DIAM 2 1
        """;
}
