using System.Text.Json.Nodes;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class ThermalExpansionOperandTests
{
    public static TheoryData<string> Codes => new("TCVA", "TCGT", "TCLT");

    [Theory]
    [InlineData("TCVA", 23.5, 10, 23.5)]
    [InlineData("TCVA", -2, 10, -2)]
    [InlineData("TCVA", 0, 10, 0)]
    [InlineData("TCGT", 23.5, 10, 10)]
    [InlineData("TCGT", -2, 10, -2)]
    [InlineData("TCGT", 10, 10, 10)]
    [InlineData("TCLT", 23.5, 10, 23.5)]
    [InlineData("TCLT", -2, 10, 10)]
    [InlineData("TCLT", 10, 10, 10)]
    public void SignedCoefficientAndBoundaryPenaltyUsePpmPerDegree(string code, double value, double target, double expected)
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[1].ThermalExpansionPpmPerC = value;
        var row = Row(code); row.Target = target;
        Assert.Equal(expected, Evaluate(optic, row), 12);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void MissingSurfaceDataFailsInsteadOfReturningZero(string code)
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[1].ThermalExpansionPpmPerC = null;
        var result = MeritFunctionCatalog.Evaluate(optic, Row(code));
        Assert.Contains("未提供", result.Error);
        Assert.True(double.IsPositiveInfinity(result.Contribution));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void RawSurfaceSlotIsAuthoritativeAndInvalidReferenceFails(string code)
    {
        var optic = Optic.CreateCookeTriplet(); optic.SurfaceGroup.Items[2].ThermalExpansionPpmPerC = 23.5;
        var row = Row(code); row.ZemaxIntegerParameters = [2, 999]; row.Target = 23.5;
        Assert.Equal(23.5, Evaluate(optic, row));
        row.ZemaxIntegerParameters[0] = 999;
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, row).Error);
    }

    [Fact]
    public void SurfaceTceRemainsIndependentOfGlassCatalogAndMaterialSelection()
    {
        var optic = Optic.CreateCookeTriplet(); var surface = optic.SurfaceGroup.Items.Single(s => s.Material == "SCHOTT:F2");
        surface.MaterialAfter = new CatalogGlassMaterial("TEST:Glass", "TEST", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.5, 1.5],
            zemaxData: new OpticalGlassDefinition { ThermalExpansionLow = 7.1 });
        var row = Row("GTCE"); row.Surface = surface.Number;
        var catalogCoefficient = Evaluate(optic, row);
        surface.ThermalExpansionPpmPerC = -3.75;
        row.Type = "TCVA"; Assert.Equal(-3.75, Evaluate(optic, row));
        row.Type = "GTCE"; Assert.Equal(catalogCoefficient, Evaluate(optic, row));
        surface.Material = "Air"; surface.MaterialAfter = new OptilandWorkbench.Core.Materials.AirMaterial();
        row.Type = "TCVA"; Assert.Equal(-3.75, Evaluate(optic, row));
        row.Type = "GTCE"; Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, row).Error);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidDataIsRejectedAtDomainAndSnapshotBoundaries(double value)
    {
        var optic = Optic.CreateBlank(); var surface = optic.SurfaceGroup.Items[1];
        surface.ThermalExpansionPpmPerC = 23.5;
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.ThermalExpansionPpmPerC = value);
        Assert.Equal(23.5, surface.ThermalExpansionPpmPerC);
        var snapshot = optic.ToSnapshot(); snapshot.Surfaces[1] = snapshot.Surfaces[1] with { ThermalExpansionPpmPerC = value };
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-2.5)]
    [InlineData(23.5)]
    public async Task CloneSnapshotAndStaroptPreserveUnknownVersusZero(double? value)
    {
        var optic = Optic.CreateBlank(); var surface = optic.SurfaceGroup.Items[1]; surface.ThermalExpansionPpmPerC = value;
        Assert.Equal(value, surface.Clone().ThermalExpansionPpmPerC);
        Assert.Equal(value, Optic.FromSnapshot(optic.ToSnapshot()).SurfaceGroup.Items[1].ThermalExpansionPpmPerC);
        var path = Path.Combine(Path.GetTempPath(), $"tce-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            Assert.Equal(value, restored.SurfaceGroup.Items[1].ThermalExpansionPpmPerC);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CompatibilityUpgradeRequiresVerifiedSurfaceDataAndPreservesDisabledState(string code)
    {
        var optic = Import(code); var row = Assert.Single(optic.MeritFunctionOperands);
        Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
        Assert.All(optic.SurfaceGroup.Items, surface => Assert.Null(surface.ThermalExpansionPpmPerC));
        var unknown = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.True(Assert.Single(unknown.MeritFunctionOperands).CompatibilityOnly);
        optic.SurfaceGroup.Items[1].ThermalExpansionPpmPerC = 23.5;
        var known = Optic.FromSnapshot(optic.ToSnapshot()); var restored = Assert.Single(known.MeritFunctionOperands);
        Assert.False(restored.CompatibilityOnly); Assert.False(restored.Enabled);
        Assert.Equal(new[] { 1, 0 }, restored.ZemaxIntegerParameters);
        Assert.Equal(new[] { 0.0, 0, 0, 0 }, restored.ZemaxDataParameters);
        Assert.Equal(23.5, restored.Target); Assert.Equal(2.5, restored.Weight);
        restored.Enabled = true; Assert.Equal(23.5, Evaluate(known, restored));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void HelpAndEditorExposeOnlyTheSurfaceReference(string code)
    {
        using var application = WorkbenchApplication.Create("cooke");
        var metadata = application.Optimization.GetMeritOperandTypes().Single(t => t.Code == code);
        Assert.False(metadata.CompatibilityOnly); Assert.Contains("TCE", metadata.Calculation);
        Assert.Single(metadata.Parameters!, parameter => parameter.IsEditable);
        var row = new MeritOperandEditorRow(new MeritOperandRowDto(1, true, code, 1, 0, 0, 0, 0, 0, 0, 23.5, 2.5, 0, 0, ""), metadata);
        row.Parameter1 = 2;
        Assert.Equal(2, row.ToDto().ZemaxInt1);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    public void EditorRejectsInvalidInputWithoutChangingValue(string text)
    {
        using var application = WorkbenchApplication.Create("cooke");
        var row = new SurfaceEditorRow(application.Prescription.GetSurfaces()[1] with { ThermalExpansionPpmPerC = 23.5 });
        Assert.Throws<FormatException>(() => row.ThermalExpansionDisplay = text);
        Assert.Equal(23.5, row.ToDto().ThermalExpansionPpmPerC);
    }

    [Fact]
    public void SurfaceEditingPublishesOneRevisionAndSupportsUndoRedo()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var row = new SurfaceEditorRow(application.Prescription.GetSurfaces()[1]);
        var original = row.ToDto(); var revision = application.Events.Revision;
        row.ThermalExpansionDisplay = "-2.5";
        application.Prescription.UpdateSurface(row.ToDto());
        Assert.Equal(revision + 1, application.Events.Revision);
        Assert.Equal(-2.5, application.Prescription.GetSurfaces()[1].ThermalExpansionPpmPerC);
        Assert.True(application.Documents.Undo());
        Assert.Equal(original.ThermalExpansionPpmPerC, application.Prescription.GetSurfaces()[1].ThermalExpansionPpmPerC);
        Assert.True(application.Documents.Redo());
        Assert.Equal(-2.5, application.Prescription.GetSurfaces()[1].ThermalExpansionPpmPerC);
        // Older DTO callers omit the appended field: an unrelated edit must not clear TCE.
        application.Prescription.UpdateSurface(original with { Label = "Changed", ThermalExpansionPpmPerC = null });
        Assert.Equal(-2.5, application.Prescription.GetSurfaces()[1].ThermalExpansionPpmPerC);
    }

    [Fact]
    public async Task NewSurfacesDefaultToZeroButLegacySnapshotMissingFieldIsUnknown()
    {
        var optic = Optic.CreateBlank(); Assert.All(optic.SurfaceGroup.Items, surface => Assert.Equal(0, surface.ThermalExpansionPpmPerC));
        var path = Path.Combine(Path.GetTempPath(), $"legacy-tce-{Guid.NewGuid():N}.json");
        Optic restored;
        try
        {
            await OpticJsonStore.SaveAsync(optic, path);
            var document = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            foreach (var surface in document["Surfaces"]!.AsArray()) surface!.AsObject().Remove("ThermalExpansionPpmPerC");
            await File.WriteAllTextAsync(path, document.ToJsonString());
            restored = await OpticJsonStore.LoadAsync(path);
        }
        finally { File.Delete(path); }
        Assert.Null(restored.SurfaceGroup.Items[1].ThermalExpansionPpmPerC);
        var row = new SurfaceEditorRow(WorkbenchMapper.ToSurfaceDto(restored.SurfaceGroup.Items[1]));
        Assert.Equal("未提供", row.ThermalExpansionDisplay);
        row.ThermalExpansionDisplay = "0"; Assert.Equal(0, row.ToDto().ThermalExpansionPpmPerC);
    }

    private static Optic Import(string code) => OpticalFormatCatalog.Import($"""
        MODE SEQ
        ENPD 10
        WAVM 1 .55 1
        SURF 0
          DISZ INFINITY
        SURF 1
          STOP
          DISZ 20
        SURF 2
          DISZ 0
        {code} 1 0 0 0 0 0 23.5 2.5 0 0
        """, ".zmx");
    private static MeritOperandDefinition Row(string code) => new() { Type = code, Surface = 1, Target = 10, Weight = 2 };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(row.Weight * Math.Pow(result.Value - row.Target, 2), result.Contribution, 8);
        return result.Value;
    }
}
