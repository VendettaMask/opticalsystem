using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class GradientIndexEditingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task AllProfilesCanBeEditedSavedReopenedAndUndone(int number)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var edit = Edit(app, number);
        if (number == 5) edit = edit with { Dispersion = new(550, 400, 700, [[.01, .001], [0, 0], [0, 0]], [[.01], [.02], [.03]]) };
        var revision = app.Events.Revision;
        app.Prescription.UpdateGradientIndexMaterial(1, edit, revision);
        var actual = app.Prescription.GetGradientIndexMaterial(1)!;
        Assert.Equal(edit.Profile, actual.Profile); Assert.Equal(edit.Coefficients, actual.Coefficients);
        Assert.Equal(edit.Integration, actual.Integration); Assert.Equal(revision + 1, app.Events.Revision);
        Assert.Equal(edit.Name, app.Prescription.GetSurfaces()[1].Material);
        Assert.Single(app.Optimization.GetMarkedVariables());
        var saved = JsonSerializer.Serialize(actual);
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with { Label = "unrelated edit" });
        Assert.Equal(saved, JsonSerializer.Serialize(app.Prescription.GetGradientIndexMaterial(1)));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            using var reopened = WorkbenchApplication.Create("cooke"); await reopened.Documents.OpenAsync(path);
            Assert.Equal(saved, JsonSerializer.Serialize(reopened.Prescription.GetGradientIndexMaterial(1)));
            Assert.Single(reopened.Optimization.GetMarkedVariables());
        }
        finally { File.Delete(path); }
        Assert.True(app.Documents.Undo()); Assert.True(app.Documents.Undo());
        Assert.Null(app.Prescription.GetGradientIndexMaterial(1));
        Assert.True(app.Documents.Redo()); Assert.Equal(saved, JsonSerializer.Serialize(app.Prescription.GetGradientIndexMaterial(1)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("nan")]
    [InlineData("bounds")]
    [InlineData("zero-minimum")]
    [InlineData("unknown")]
    [InlineData("budget")]
    [InlineData("air")]
    [InlineData("mirror")]
    [InlineData("dispersion")]
    [InlineData("axis")]
    public void InvalidEditsLeaveMaterialRevisionAndUndoUnchanged(string fault)
    {
        using var app = WorkbenchApplication.Create("cooke"); var edit = Edit(app, fault == "axis" ? 1 : 5);
        var coefficients = edit.Coefficients.ToArray();
        edit = fault switch
        {
            "missing" => edit with { Coefficients = coefficients.Skip(1).ToArray() },
            "duplicate" => edit with { Coefficients = coefficients.Append(coefficients[0]).ToArray() },
            "nan" => edit with { Coefficients = coefficients.Select((c, i) => i == 0 ? c with { Value = double.NaN } : c).ToArray() },
            "bounds" => edit with { Coefficients = coefficients.Select((c, i) => i == 0 ? c with { Minimum = 2, Maximum = 1 } : c).ToArray() },
            "zero-minimum" => edit with { Coefficients = coefficients.Select((c, i) => i == 0 ? c with { Minimum = 0 } : c).ToArray() },
            "unknown" => edit with { Coefficients = coefficients.Select((c, i) => i == 0 ? c with { Key = "fake" } : c).ToArray() },
            "budget" => edit with { Integration = edit.Integration with { MaximumStep = -1 } },
            "air" => edit with { Name = " Air " },
            "mirror" => edit with { Name = "MIRROR" },
            "dispersion" => edit with { Dispersion = new(550, 400, 500, [[0], [0], [0]], [[0], [0], [0]]) },
            "axis" => edit with { Coefficients = coefficients.Select(c => c.Key == "nr1" ? c with { Value = .1 } : c).ToArray() },
            _ => throw new Exception()
        };
        var revision = app.Events.Revision; var surfaces = JsonSerializer.Serialize(app.Prescription.GetSurfaces(), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
        Assert.ThrowsAny<Exception>(() => app.Prescription.UpdateGradientIndexMaterial(1, edit, revision));
        Assert.Equal(revision, app.Events.Revision); Assert.Equal(surfaces, JsonSerializer.Serialize(app.Prescription.GetSurfaces(), new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        Assert.Null(app.Prescription.GetGradientIndexMaterial(1)); Assert.False(app.Documents.Undo());
    }

    [Theory]
    [InlineData(1, "nr1")]
    [InlineData(4, "nx1")]
    [InlineData(4, "nx2")]
    [InlineData(4, "ny1")]
    [InlineData(4, "ny2")]
    public void UnsupportedIndependentVariablesAreExplainedAndRejected(int profile, string key)
    {
        using var app = WorkbenchApplication.Create("cooke"); var edit = Edit(app, profile);
        Assert.NotNull(app.Prescription.GetGradientIndexProfiles()[profile - 1].Coefficients.Single(c => c.Key == key).VariableDisabledReason);
        edit = edit with { Coefficients = edit.Coefficients.Select(c => c.Key == key ? c with { Variable = true, Minimum = -.1, Maximum = .1 } : c).ToArray() };
        Assert.Throws<NotSupportedException>(() => app.Prescription.UpdateGradientIndexMaterial(1, edit, app.Events.Revision));
        Assert.Null(app.Prescription.GetGradientIndexMaterial(1));
    }

    [Fact]
    public void StaleEditorCannotOverwriteLaterChanges()
    {
        using var app = WorkbenchApplication.Create("cooke"); var edit = Edit(app, 5); var revision = app.Events.Revision;
        app.Prescription.UpdateSurface(app.Prescription.GetSurfaces()[1] with { Label = "later" });
        Assert.Throws<InvalidOperationException>(() => app.Prescription.UpdateGradientIndexMaterial(1, edit, revision));
        Assert.Null(app.Prescription.GetGradientIndexMaterial(1)); Assert.Equal("later", app.Prescription.GetSurfaces()[1].Label);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndpointSurfacesCannotOwnGradientVolumes(bool image)
    {
        using var app = WorkbenchApplication.Create("cooke"); var revision = app.Events.Revision;
        var number = image ? app.Prescription.GetSurfaces().Count - 1 : 0;
        Assert.Throws<NotSupportedException>(() => app.Prescription.UpdateGradientIndexMaterial(number, Edit(app, 5), revision));
        Assert.Equal(revision, app.Events.Revision);
    }

    [Fact]
    public async Task LinkedMaterialUpdatesPropagateCompleteDataAndFollowingMedium()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.UpdateGradientIndexMaterial(1, Edit(app, 5), app.Events.Revision);
        var index = app.MultiConfiguration.Add();
        var edit = app.Prescription.GetGradientIndexMaterial(1)!;
        edit = edit with { Coefficients = edit.Coefficients.Select((c, i) => i == 0 ? c with { Value = 1.6 } : c).ToArray() };
        app.Prescription.UpdateGradientIndexMaterial(1, edit, app.Events.Revision);
        app.MultiConfiguration.Activate(index);
        Assert.Equal(1.6, app.Prescription.GetGradientIndexMaterial(1)!.Coefficients[0].Value);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await app.Documents.SaveAsync(path);
            var document = await StarOptProjectStore.LoadAsync(path);
            foreach (var optic in document.Configurations)
                Assert.Equal(1.6, Assert.IsType<GradientIndexMaterial>(optic.SurfaceGroup.Items[2].MaterialBefore).RefractiveIndex(new(), 550));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("nan")]
    [InlineData("outside")]
    [InlineData("nested")]
    [InlineData("text")]
    public void InvalidVariableSnapshotsFailStrictly(string fault)
    {
        var snapshot = Plate().ToSnapshot(); var material = snapshot.Surfaces[1].Components!.MaterialAfterComponent!;
        var variable = material.Children!["variables"];
        switch (fault)
        {
            case "missing": variable.Numbers.Remove("n0_min"); break;
            case "extra": variable.Numbers["fake_min"] = 0; variable.Numbers["fake_max"] = 1; break;
            case "nan": variable.Numbers["n0_min"] = double.NaN; break;
            case "outside": variable.Numbers["n0_min"] = 1.7; break;
            case "nested": material.Children["variables"] = variable with { Children = new() { ["other"] = variable } }; break;
            case "text": variable.Text["unrecognized"] = "value"; break;
        }
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(snapshot));
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
    }

    [Fact]
    public void CoreUpdatesKeepAdjacentIndexMetadataAndDoNotMutateOtherOptics()
    {
        var optic = Plate(); var surface = optic.SurfaceGroup.Items[1];
        var clone = Optic.FromSnapshot(optic.ToSnapshot());
        var variable = GradientIndexParameters.CreateVariable(optic, surface, "n0");
        GradientIndexParameters.Write(optic, surface, "n0", 1.6);
        Assert.Equal(1.6, GradientIndexParameters.Read(surface, "n0"));
        Assert.Equal(1.6, Assert.IsType<GradientIndexMaterial>(optic.SurfaceGroup.Items[2].MaterialBefore).RefractiveIndex(new(), 550));
        Assert.Equal(1.5, GradientIndexParameters.Read(clone.SurfaceGroup.Items[1], "n0"));
        Assert.Throws<ArgumentException>(() => GradientIndexParameters.Write(optic, clone.SurfaceGroup.Items[1], "n0", 1.7));
        Assert.Equal(1.5, GradientIndexParameters.Read(clone.SurfaceGroup.Items[1], "n0"));
        Assert.Throws<ArgumentException>(() => GradientIndexParameters.Write(optic, surface, "n0", 2.5));
        Assert.Equal(1.6, GradientIndexParameters.Read(surface, "n0"));
    }

    [Fact]
    public void MultiConfigurationSameNameEditsStayDetachedAndClearAllIsUndoable()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Prescription.UpdateGradientIndexMaterial(1, Edit(app, 5), app.Events.Revision);
        var index = app.MultiConfiguration.Add(); app.MultiConfiguration.Activate(index);
        ChangeBaseIndex(1.6); app.MultiConfiguration.Activate(0); ChangeBaseIndex(1.7);
        app.MultiConfiguration.Activate(index); Assert.Equal(1.6, Value());
        app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll);
        Assert.Empty(app.Optimization.GetMarkedVariables());
        Assert.True(app.Documents.Undo()); Assert.Single(app.Optimization.GetMarkedVariables());
        Assert.True(app.Documents.Redo()); Assert.Empty(app.Optimization.GetMarkedVariables());
        app.MultiConfiguration.Activate(0); Assert.Equal(1.7, Value()); Assert.Empty(app.Optimization.GetMarkedVariables());
        double Value() => app.Prescription.GetGradientIndexMaterial(1)!.Coefficients[0].Value;
        void ChangeBaseIndex(double value)
        {
            var edit = app.Prescription.GetGradientIndexMaterial(1)!;
            app.Prescription.UpdateGradientIndexMaterial(1, edit with { Coefficients = edit.Coefficients.Select((c, i) => i == 0 ? c with { Value = value } : c).ToArray() }, app.Events.Revision);
        }
    }

    [Fact]
    public async Task ProductionOptimizationSolvesTwoCoefficientsWithDistinctResultsAndUndo()
    {
        using var app = WorkbenchApplication.Create("cooke"); var edit = Edit(app, 5);
        edit = edit with { Coefficients = edit.Coefficients.Select(c => c.Key == "nz1" ? c with { Variable = true, Minimum = -.01, Maximum = .01 } : c).ToArray() };
        app.Prescription.UpdateGradientIndexMaterial(1, edit, app.Events.Revision);
        var thickness = app.Prescription.GetSurfaces()[1].Thickness;
        app.Optimization.SetMeritFunction([Operand(1, "I1VA", 1.55), Operand(2, "I4VA", 1.55 + .002 * thickness)]);
        var result = await app.Optimization.OptimizeVariablesAsync("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-8, result.Message);
        Assert.Equal(new[] { "n0", "nz1" }, result.Variables.Select(v => v.Parameter).Order().ToArray());
        Assert.InRange(Math.Abs(result.Variables.Single(v => v.Parameter == "n0").FinalValue - 1.55), 0, 1e-7);
        Assert.InRange(Math.Abs(result.Variables.Single(v => v.Parameter == "nz1").FinalValue - .002), 0, 1e-7);
        Assert.True(app.Documents.Undo()); Assert.Equal(1.5, app.Prescription.GetGradientIndexMaterial(1)!.Coefficients[0].Value);
        Assert.True(app.Documents.Redo()); Assert.InRange(app.Prescription.GetGradientIndexMaterial(1)!.Coefficients[0].Value, 1.5499999, 1.5500001);
    }

    internal static GradientIndexMaterialEditDto Edit(WorkbenchApplication app, int number) => new("GRIN test", $"gradient{number}",
        app.Prescription.GetGradientIndexProfiles()[number - 1].Coefficients.Select(c => new GradientIndexCoefficientEditDto(c.Key, c.DefaultValue,
            c.Key is "n0" or "n0Squared", c.Key == "n0Squared" ? 1 : c.Key == "n0" ? 1 : -.1, c.Key == "n0Squared" ? 4 : c.Key == "n0" ? 2 : .1)).ToArray(),
        null, new());
    private static MeritOperandRowDto Operand(int index, string type, double target) => new(index, true, type, 1, 1, 1, 0, 0, 0, 0, target, 1, 0, 0, "",
        ZemaxInt1: 1, ZemaxInt2: 1, ZemaxData1: 0, ZemaxData2: 0, ZemaxData3: 0, ZemaxData4: 0);
    private static Optic Plate()
    {
        var optic = new Optic("GRIN variable"); optic.Fields.Add(new()); optic.Wavelengths.Add(new() { Nanometers = 550, IsPrimary = true });
        var material = new GradientIndexMaterial("GRIN test", new Gradient5IndexProfile(1.5), 100,
            variables: new Dictionary<string, GradientIndexVariableRange> { ["n0"] = new(1, 2) });
        optic.SurfaceGroup.Replace([new() { Thickness = double.PositiveInfinity }, new() { Thickness = 8, MaterialAfter = material, IsStop = true, SemiDiameter = 1, SemiDiameterFixed = true },
            new() { MaterialBefore = material, Thickness = 5, SemiDiameter = 1, SemiDiameterFixed = true }, new() { SemiDiameter = 1, SemiDiameterFixed = true }]);
        return optic;
    }
}
