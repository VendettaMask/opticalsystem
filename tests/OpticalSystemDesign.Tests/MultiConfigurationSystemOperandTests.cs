using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Multiconfig;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Serialization;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace OpticalSystemDesign.Tests;

public sealed class MultiConfigurationSystemOperandTests
{
    [Theory]
    [InlineData(MultiConfigurationOperandKind.FieldX, 2, -3)]
    [InlineData(MultiConfigurationOperandKind.FieldY, -2, 3)]
    [InlineData(MultiConfigurationOperandKind.FieldWeight, .2, 1.1)]
    [InlineData(MultiConfigurationOperandKind.Wavelength, .45, .65)]
    [InlineData(MultiConfigurationOperandKind.WavelengthWeight, .2, 1.1)]
    public void IndependentCellsVariablesPickupsAndSurfaceInsertionKeepTheirActualTarget(MultiConfigurationOperandKind kind, double first, double second)
    {
        var row = new MultiConfigurationOperand(kind, 9);
        var system = new MultiConfiguration([OpticWithNineTargets(), OpticWithNineTargets()], operandRows: [row]);
        var original = row.Read(system.Configurations[1]);
        system.SetOperandValue(1, 0, first); Assert.Equal(original, row.Read(system.Configurations[1]));
        system.SetOperandValue(1, 1, second); system.SetOperandVariable(1, 1, true);
        system.InsertSurface(2); system.RemoveSurface(2);
        Assert.Equal(row, Assert.Single(system.OperandRows));
        Assert.Equal(row, Assert.Single(system.OperandVariables).Operand);
        system.SetOperandVariable(1, 1, false);
        system.SetOperandPickup(1, 1, 1, 0, .5, second - first * .5);
        Assert.Equal(second, row.Read(system.Configurations[1]), 10);
        system.SetOperandValue(1, 0, first + .1);
        Assert.Equal(second + .05, row.Read(system.Configurations[1]), 10);
        Assert.Throws<InvalidOperationException>(() => system.SetOperandValue(1, 1, first));
        Assert.Throws<InvalidOperationException>(() => system.SetOperandVariable(1, 1, true));
        system.InsertSurface(2); system.RemoveSurface(2);
        Assert.Equal(row, Assert.Single(system.OperandPickups).SourceOperand);
        system.ClearOperandPickup(1, 1);
        system.SetOperandVariable(1, 1, true); var clone = system.AddConfiguration(1);
        Assert.Contains(system.OperandVariables, variable => variable.ConfigurationIndex == clone && variable.Operand == row);
        Assert.Equal(row.Read(system.Configurations[1]), row.Read(system.Configurations[clone]));
    }

    [Theory]
    [InlineData(MultiConfigurationOperandKind.FieldX, 2, -3)]
    [InlineData(MultiConfigurationOperandKind.FieldY, -2, 3)]
    [InlineData(MultiConfigurationOperandKind.FieldWeight, .2, 1.1)]
    [InlineData(MultiConfigurationOperandKind.Wavelength, .45, .65)]
    [InlineData(MultiConfigurationOperandKind.WavelengthWeight, .2, 1.1)]
    public void ProductionDlsOptimizesBothConfigurationsAndUndoRestoresTheWholeDocument(MultiConfigurationOperandKind kind, double first, double second)
    {
        var runtime = new WorkbenchRuntime(OpticWithNineTargets()); runtime.AddMultiConfiguration();
        var row = new MultiConfigurationOperand(kind, 9);
        runtime.ReplaceMultiConfigurationOperands([row]);
        runtime.SetMultiConfigurationOperandVariable(1, 0, true); runtime.SetMultiConfigurationOperandVariable(1, 1, true);
        Assert.Equal(2, runtime.GetMarkedOptimizationVariables().Count);
        runtime.ReplaceMeritFunction([Mco(1, first), Mco(2, second)]);
        var before = row.Read(runtime.CurrentOptic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 50);
        Assert.True(result.FinalMerit < 1e-8, result.FinalMerit.ToString("G17"));
        var context = runtime.CreateMeritConfigurationContext();
        Assert.Equal(first, row.Read(context.Configurations[0]), 4); Assert.Equal(second, row.Read(context.Configurations[1]), 4);
        Assert.True(runtime.Undo()); Assert.Equal(before, row.Read(runtime.CurrentOptic));
        Assert.True(runtime.Redo()); Assert.Equal(first, row.Read(runtime.CurrentOptic), 4);
    }

    [Theory]
    [InlineData(MultiConfigurationOperandKind.Wavelength, 0)]
    [InlineData(MultiConfigurationOperandKind.Wavelength, .0005)]
    [InlineData(MultiConfigurationOperandKind.WavelengthWeight, -1)]
    [InlineData(MultiConfigurationOperandKind.FieldWeight, -1)]
    [InlineData(MultiConfigurationOperandKind.FieldX, double.NaN)]
    [InlineData(MultiConfigurationOperandKind.FieldY, double.PositiveInfinity)]
    public void InvalidEditsAndFailedPickupPropagationAreAtomic(MultiConfigurationOperandKind kind, double value)
    {
        var row = new MultiConfigurationOperand(kind, 9);
        var system = new MultiConfiguration([OpticWithNineTargets(), OpticWithNineTargets()], operandRows: [row]);
        var initial = row.Read(system.Configurations[0]);
        Assert.ThrowsAny<ArgumentException>(() => system.SetOperandValue(1, 0, value));
        Assert.Equal(initial, row.Read(system.Configurations[0]));
        if (double.IsFinite(value))
        {
            Assert.ThrowsAny<ArgumentException>(() => system.SetOperandPickup(1, 1, 1, 0, 0, value));
            Assert.Empty(system.OperandPickups); Assert.Equal(initial, row.Read(system.Configurations[1]));
        }
    }

    [Fact]
    public async Task ImportedFiveCodesRetainValuesVariablesUnitsNativeSaveAndApplicationUndo()
    {
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(ImportText);
        Assert.Equal(new[] { "XFIE", "YFIE", "FLWT", "WAVE", "WLWT" }, imported.OperandRows.Select(row => MultiConfigurationOperand.Code(row.Kind)));
        Assert.Equal(5, imported.OperandVariables.Count);
        Assert.Equal(new[] { 4d, 8d, .5, .65, .25 }, imported.OperandRows.Select(row => row.Read(imported.Configurations[1])));
        var path = Path.Combine(Path.GetTempPath(), $"mce-system-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument(imported.Configurations, 0,
                imported.BrokenLinks, OperandRows: imported.OperandRows, OperandVariables: imported.OperandVariables), path);
            using var app = WorkbenchApplication.Create("cooke"); await app.Documents.OpenAsync(path);
            var rows = app.MultiConfiguration.GetOperandRows();
            Assert.Equal("°", rows[0].Units![1]); Assert.Equal("µm", rows[3].Units![1]);
            app.MultiConfiguration.SetOperandValue(4, 1, .71, app.Events.Revision);
            Assert.Equal(.71, app.MultiConfiguration.GetOperandRows()[3].Values[1]);
            Assert.True(app.Documents.Undo()); Assert.Equal(.65, app.MultiConfiguration.GetOperandRows()[3].Values[1]);
            Assert.True(app.Documents.Redo()); await app.Documents.SaveAsync(path);
            var restored = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(.71, restored.OperandRows![3].Read(restored.Configurations[1]));
            Assert.Equal(5, restored.OperandVariables!.Count);
            // A valid checksum cannot disguise the new target semantics as an
            // older project version whose row indices only described surfaces.
            var native = await File.ReadAllBytesAsync(path);
            using var source = new MemoryStream(native, 52, BinaryPrimitives.ReadInt32LittleEndian(native.AsSpan(16, 4)));
            using var inflater = new BrotliStream(source, CompressionMode.Decompress);
            var payload = JsonNode.Parse(inflater)!; Assert.Equal(32, payload["FormatVersion"]!.GetValue<int>());
            payload["FormatVersion"] = 31;
            var json = System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString());
            using var compressed = new MemoryStream();
            using (var deflater = new BrotliStream(compressed, CompressionLevel.Optimal, true)) deflater.Write(json);
            var forged = new byte[52 + compressed.Length]; native.AsSpan(0, 52).CopyTo(forged);
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(12, 4), json.Length);
            BinaryPrimitives.WriteInt32LittleEndian(forged.AsSpan(16, 4), (int)compressed.Length);
            SHA256.HashData(json).CopyTo(forged, 20); compressed.ToArray().CopyTo(forged, 52);
            await File.WriteAllBytesAsync(path, forged);
            await Assert.ThrowsAsync<InvalidDataException>(() => StarOptProjectStore.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("WAVE 1 2 0")]
    [InlineData("WLWT 1 2 -1")]
    [InlineData("FLWT 1 2 -1")]
    [InlineData("XFIE 99 2 1")]
    public void ImportRejectsInvalidValuesAndMissingTargets(string command) =>
        Assert.ThrowsAny<Exception>(() => new ZemaxZmxImporter().ImportConfigurationSet(ImportText + "\n" + command));

    [Fact]
    public void WavelengthEditsInvalidatePhysicalComputationAndHeightFieldsKeepMillimeters()
    {
        var optic = OpticWithNineTargets();
        var primary = optic.Wavelengths.Select((wave, index) => (wave, index)).Single(p => p.wave.IsPrimary).index + 1;
        var system = new MultiConfiguration(optic); var row = new MultiConfigurationOperand(MultiConfigurationOperandKind.Wavelength, primary);
        system.ReplaceOperandRows([row]);
        system.SetOperandValue(1, 0, .45); var blue = system.Configurations[0].Paraxial.EstimateEffectiveFocalLength();
        system.SetOperandValue(1, 0, .65); var red = system.Configurations[0].Paraxial.EstimateEffectiveFocalLength();
        Assert.True(Math.Abs(blue - red) > 1e-4);
        Assert.Equal(red, Optic.FromSnapshot(system.Configurations[0].ToSnapshot()).Paraxial.EstimateEffectiveFocalLength(), 10);
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(ImportText.Replace("UNIT MM", "UNIT CM").Replace("FTYP 0", "FTYP 1"));
        Assert.Equal(40, imported.Configurations[1].Fields[0].X);
        Assert.Equal(80, imported.Configurations[1].Fields[0].Y);
        Assert.Equal(.65, imported.Configurations[1].Wavelengths[0].Micrometers);
    }

    [Fact]
    public void OrdinarySystemEditsPropagatePickupsAndRejectReferenceBreakingRemoval()
    {
        using var app = WorkbenchApplication.Create("cooke"); app.MultiConfiguration.Add();
        app.MultiConfiguration.ReplaceOperandRows([new(MultiConfigurationParameterKind.FieldX, 2),
            new(MultiConfigurationParameterKind.Wavelength, 2)], app.Events.Revision);
        app.MultiConfiguration.SetOperandPickup(1, 1, new(1, 0, 2, 1), app.Events.Revision);
        app.Prescription.UpdateField(new FieldRowDto(1, "Field 2", 3, 6, 0, 0, .75));
        Assert.Equal(7, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
        app.MultiConfiguration.Activate(1);
        var revision = app.Events.Revision;
        Assert.Throws<InvalidOperationException>(() => app.Prescription.UpdateField(new FieldRowDto(1, "Field 2", 4, 6, 0, 0, .75)));
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(7, app.MultiConfiguration.GetOperandRows()[0].Values[1]);
        Assert.Throws<InvalidOperationException>(() => app.Prescription.RemoveField(0));
        Assert.Throws<InvalidOperationException>(() => app.Prescription.RemoveWavelength(1));
        Assert.Equal(revision, app.Events.Revision);
    }

    private static MeritOperandDefinition Mco(int configuration, double target) => new()
    { Type = "MCOV", Surface = 1, Wavelength = configuration, Target = target, Weight = 1, ZemaxIntegerParameters = [1, configuration], ZemaxDataParameters = [0, 0, 0, 0] };

    internal static Optic OpticWithNineTargets()
    {
        var optic = Optic.CreateCookeTriplet(); optic.Solves.KeepImageAtBackFocus = false;
        foreach (var surface in optic.SurfaceGroup.Items) { surface.RadiusVariable = false; surface.ThicknessVariable = false; }
        while (optic.Fields.Count < 9) optic.Fields.Add(new FieldPoint());
        while (optic.Wavelengths.Count < 9) optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = false });
        return optic;
    }

    internal const string ImportText = """
        NAME System operands
        MODE SEQ
        UNIT MM
        ENPD 5
        FTYP 0 0 1 1 0 0 0
        XFLN 0
        YFLN 0
        WAVM 1 0.55 1
        MNUM 2 1
        XFIE 1 1 2 0
        XFIE 1 2 4 1
        YFIE 1 1 3 0
        YFIE 1 2 8 1
        FLWT 1 1 1 0
        FLWT 1 2 .5 1
        WAVE 1 1 .55 0
        WAVE 1 2 .65 1
        WLWT 1 1 1 0
        WLWT 1 2 .25 1
        SURF 0
        TYPE STANDARD
        DISZ INFINITY
        SURF 1
        TYPE STANDARD
        STOP
        CURV .02
        DISZ 5
        DIAM 5
        SURF 2
        TYPE STANDARD
        CURV -.02
        DISZ 20
        DIAM 5
        SURF 3
        TYPE STANDARD
        CURV 0
        DISZ 0
        DIAM 5
        """;
}
