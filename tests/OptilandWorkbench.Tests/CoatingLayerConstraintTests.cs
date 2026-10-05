using System.Numerics;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class CoatingLayerConstraintTests
{
    public static IEnumerable<object[]> Codes => new[] { "CM", "CI", "CE" }.SelectMany(prefix =>
        new[] { "GT", "LT", "VA" }.Select(suffix => new object[] { prefix + suffix }));

    [Theory]
    [MemberData(nameof(Codes))]
    public void IndividualLayerValuesAndBoundariesUseTheStoredAdjustment(string code)
    {
        var optic = CreateOptic(); var expected = code[1] switch { 'M' => 1.2, 'I' => .15, _ => .02 };
        var row = Row(code, 1, 1, expected);
        Near(expected, Value(optic, row));
        row.Target = expected + .1;
        Near(code.EndsWith("LT") ? row.Target : expected, Value(optic, row));
        row.Target = expected - .1;
        Near(code.EndsWith("GT") ? row.Target : expected, Value(optic, row));
        var type = ZemaxOperandRegistry.Get(code); Assert.Equal(6, type.Parameters.Count);
        Assert.Equal(ZemaxOperandSupportLevel.Executable, type.SupportLevel);
    }

    [Theory]
    [InlineData("CMGT", .7)]
    [InlineData("CMLT", 1.8)]
    [InlineData("CIGT", -.1)]
    [InlineData("CILT", .3)]
    [InlineData("CEGT", -.01)]
    [InlineData("CELT", .1)]
    public void WildcardBoundsUseTheWorstLayerAndRejectMissingSpecifiedLayers(string code, double expected)
    {
        var optic = CreateOptic(); var target = code.EndsWith("GT") ? 10 : -10;
        Near(expected, Value(optic, Row(code, 0, 0, target)));
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, Row(code, 0, 2, target)).Error);
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, Row(code, 3, 1, target)).Error);
        foreach (var surface in optic.SurfaceGroup.Items) surface.CoatingModel = new NoneCoatingModel();
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, Row(code, 0, 0, target)).Error);
    }

    [Theory]
    [InlineData("CMVA")]
    [InlineData("CIVA")]
    [InlineData("CEVA")]
    public void ValueOperandsDoNotInventAnAllLayerAverage(string code)
    {
        foreach (var (surface, layer) in new[] { (0, 1), (1, 0), (-1, 1), (1, -1), (99, 1), (1, 99) })
            Assert.NotEmpty(MeritFunctionCatalog.Evaluate(CreateOptic(), Row(code, surface, layer)).Error);
    }

    [Fact]
    public void WildcardNeverSilentlyOmitsAnUnsupportedCoatingAndCancellationPropagates()
    {
        var optic = CreateOptic(); optic.SurfaceGroup.Items[2].CoatingModel = new SimpleCoatingModel(.9, .1);
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, Row("CMGT", 0, 0, 1)).Error);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, Row("CMVA", 1, 1)));
    }

    [Theory]
    [InlineData(450)]
    [InlineData(550)]
    [InlineData(650)]
    public void DispersionOffsetsAndThicknessMultiplierMatchIndependentNormalIncidenceAiry(double wavelength)
    {
        var optic = CreateOptic();
        var material = new CauchyMaterial("dispersive film", 1.7, .01);
        var film = new CoherentFilm(material, 80, new(1.3, .2, .03));
        optic.SurfaceGroup.Items[1].CoatingModel = new CoherentMultilayerCoating([film]);
        optic.SurfaceGroup.Items[2].CoatingModel = new NoneCoatingModel();
        optic.Wavelengths[0].Nanometers = wavelength;
        var n = new Complex(1.9 + .01 / Math.Pow(wavelength / 1000, 2), .03);
        var r01 = (1 - n) / (1 + n); var r12 = (n - 1.5) / (n + 1.5);
        var e = Complex.Exp(Complex.ImaginaryOne * 2 * Math.PI * n * 104 / wavelength);
        var denominator = 1 + r01 * r12 * e * e;
        var reflection = (r01 + r12 * e * e) / denominator;
        var transmission = 2 / (1 + n) * 2 * n / (n + 1.5) * e / denominator * Math.Sqrt(1.5);
        Near(reflection.Magnitude * reflection.Magnitude, Value(optic, Coda(-1)));
        Near(transmission.Magnitude * transmission.Magnitude, Value(optic, Coda(-2)));
        var stored = CoatingLayerMetrics.Layer(optic.SurfaceGroup.Items[1], 1);
        Near(80, stored.ThicknessNanometers); Near(material.RefractiveIndex(wavelength), stored.Material.RefractiveIndex(wavelength));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void InvalidAdjustmentsCannotEnterThePhysicalCalculation(int problem)
    {
        var p = problem switch
        {
            0 => new CoatingLayerParameters(-1),
            1 => new(11),
            2 => new(double.NaN),
            3 => new(IndexOffset: double.PositiveInfinity),
            4 => new(IndexOffset: -5),
            _ => new(ExtinctionOffset: -.2)
        };
        Assert.Throws<ArgumentException>(() =>
        {
            var optic = CreateOptic(); var coating = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film", 1.5), 100, p)]);
            CoatingLayerMetrics.ValidateAtSystemWavelengths(optic, coating);
        });
        Assert.Throws<ArgumentException>(() => new CoherentMultilayerCoating([new(new AirMaterial(), 0, new(MultiplierVariable: true))]));
    }

    [Fact]
    public void ImmutableReplacementSeparatesSurfacesAndSnapshotsAndInvalidatesTracing()
    {
        var optic = CreateOptic(); var original = (CoherentMultilayerCoating)optic.SurfaceGroup.Items[1].CoatingModel;
        optic.SurfaceGroup.Items[2].CoatingModel = original;
        var copy = Optic.FromSnapshot(optic.ToSnapshot()); var before = Value(optic, Coda(-2));
        CoatingLayerMetrics.Write(optic, optic.SurfaceGroup.Items[1], 1, CoatingLayerParameter.Multiplier, 1.9);
        Assert.True(Math.Abs(before - Value(optic, Coda(-2))) > 1e-4);
        Near(1.2, original.Layers[0].Adjustment.Multiplier);
        Near(1.2, CoatingLayerMetrics.Layer(optic.SurfaceGroup.Items[2], 1).Adjustment.Multiplier);
        Near(before, Value(copy, Coda(-2)));
        Near(1.9, CoatingLayerMetrics.Layer(Optic.FromSnapshot(optic.ToSnapshot()).SurfaceGroup.Items[1], 1).Adjustment.Multiplier);
    }

    [Theory]
    [InlineData("multiplier_0", 11)]
    [InlineData("indexOffset_0", double.NaN)]
    [InlineData("extinctionVariable_0", 2)]
    [InlineData("indexVariable_0", -.5)]
    public void MalformedLayerSnapshotsFailAndOldSnapshotsRetainUnperturbedDefaults(string key, double invalid)
    {
        var optic = CreateOptic(); var snapshot = optic.ToSnapshot();
        var component = snapshot.Surfaces[1].Components!.Coating!;
        var values = Assert.IsType<Dictionary<string, double>>(component.Numbers); values[key] = invalid;
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
        snapshot = optic.ToSnapshot(); component = snapshot.Surfaces[1].Components!.Coating!;
        values = Assert.IsType<Dictionary<string, double>>(component.Numbers);
        foreach (var k in values.Keys.Where(k => k != "count" && !k.StartsWith("thickness_")).ToArray()) values.Remove(k);
        Assert.Equal(new CoatingLayerParameters(), CoatingLayerMetrics.Layer(Optic.FromSnapshot(snapshot).SurfaceGroup.Items[1], 1).Adjustment);
    }

    [Theory]
    [InlineData("CMVA", CoatingLayerParameter.Multiplier, 1.6)]
    [InlineData("CIVA", CoatingLayerParameter.IndexOffset, .3)]
    [InlineData("CEVA", CoatingLayerParameter.ExtinctionOffset, .1)]
    public void ProductionDlsOptimizesEachMarkedLayerVariable(string code, CoatingLayerParameter kind, double target)
    {
        var optic = CreateOptic(); var surface = optic.SurfaceGroup.Items[1];
        var p = CoatingLayerMetrics.Layer(surface, 1).Adjustment with
        { MultiplierVariable = kind == CoatingLayerParameter.Multiplier, IndexVariable = kind == CoatingLayerParameter.IndexOffset, ExtinctionVariable = kind == CoatingLayerParameter.ExtinctionOffset };
        surface.CoatingModel = ((CoherentMultilayerCoating)surface.CoatingModel).WithLayerParameters(1, p);
        optic.MeritFunctionOperands.Add(Row(code, 1, 1, target));
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.InitialMerit > 1e-4); Assert.True(result.FinalMerit < 1e-10);
        Near(target, Value(runtime.CurrentOptic, Row(code, 1, 1)), 1e-5);
    }

    [Fact]
    public void ProductionDlsUsesCoatingVariablesToOptimizeActualCodaTransmission()
    {
        var optic = CreateOptic(); var surface = optic.SurfaceGroup.Items[1];
        surface.CoatingModel = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film", 1.8), 100, new(ExtinctionOffset: .08))]);
        var row = Coda(-2); row.Target = Value(optic, row);
        surface.CoatingModel = ((CoherentMultilayerCoating)surface.CoatingModel).WithLayerParameters(1, new(ExtinctionOffset: .03, ExtinctionVariable: true));
        optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 50);
        Assert.True(result.InitialMerit > 1e-4); Assert.True(result.FinalMerit < result.InitialMerit * 1e-6);
        Near(.08, CoatingLayerMetrics.Layer(runtime.CurrentOptic.SurfaceGroup.Items[1], 1).Adjustment.ExtinctionOffset, 2e-5);
    }

    [Theory]
    [InlineData(".zmx")]
    [InlineData(".seq")]
    [InlineData(".len")]
    [InlineData(".txt")]
    public void UnmappedPhysicalCoatingDataCannotBeSilentlyExportedAsAName(string extension) =>
        Assert.Contains("STAROPT", Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(CreateOptic(), extension)).Message);

    [Theory]
    [MemberData(nameof(Codes))]
    public void NativeRowsStayDisabledUntilCoatingMappingIsVerified(string code)
    {
        var optic = OpticalFormatCatalog.Import($"MODE SEQ\nENPD 2\nWAVM 1 .55 1\nSURF 0\n DISZ INFINITY\nSURF 1\n STOP\n DISZ 5\nSURF 2\n DISZ 0\n{code} 1 1 0 0 0 0 .2 1 91 92\n", ".zmx");
        var row = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled); Assert.Contains("91 92", row.Comment);
    }

    [Fact]
    public async Task ApplicationEditingPreservesBaseMaterialsThroughReorderingSaveUndoAndStaleRejection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coating-layers-{Guid.NewGuid():N}.staropt");
        try
        {
            var optic = CreateOptic();
            foreach (var code in Codes.Select(row => (string)row[0])) optic.MeritFunctionOperands.Add(Row(code, 1, 1));
            await StarOptProjectStore.SaveAsync(new([optic], 0), path);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            var original = app.Prescription.GetCoatingLayers(1); var revision = app.Events.Revision;
            var reordered = new[] { original[1] with { IndexOffset = .4, IndexVariable = true }, original[0] };
            app.Prescription.UpdateCoatingLayers(1, reordered, revision);
            Assert.Equal(revision + 1, app.Events.Revision); Assert.Equal(.4, app.Prescription.GetCoatingLayers(1)[0].IndexOffset);
            Assert.Throws<InvalidOperationException>(() => app.Prescription.UpdateCoatingLayers(1, original, revision));
            Assert.True(app.Documents.Undo()); Assert.Equal(original, app.Prescription.GetCoatingLayers(1));
            Assert.True(app.Documents.Redo());
            var types = app.Optimization.GetMeritOperandTypes();
            var edits = app.Optimization.GetMeritFunction().Select(row =>
            {
                var type = types.Single(t => t.Code == row.Type);
                Assert.False(type.CompatibilityOnly); Assert.Equal(6, type.Parameters!.Count);
                var editor = new MeritOperandEditorRow(row, type);
                Assert.True(editor.IsParameterEditable(1)); Assert.False(editor.IsParameterEditable(2));
                editor.Parameter2 = 2;
                return editor.ToDto();
            }).ToArray();
            app.Optimization.SetMeritFunction(edits);
            await app.Documents.SaveAsync(path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var layers = CoatingLayerMetrics.RequireCoating(restored.SurfaceGroup.Items[1]).Layers;
            Near(1.7, layers[0].Material.RefractiveIndex(550)); Assert.True(layers[0].Adjustment.IndexVariable);
            Assert.Equal(9, restored.MeritFunctionOperands.Count);
            foreach (var row in restored.MeritFunctionOperands)
            {
                Assert.Equal(2, row.ZemaxIntegerParameters[1]);
                Assert.Empty(MeritFunctionCatalog.Evaluate(restored, row).Error);
            }
            revision = app.Events.Revision;
            Assert.Throws<ArgumentException>(() => app.Prescription.UpdateCoatingLayers(1, [new("missing-coating-test-material", 100)], revision));
            Assert.Equal(revision, app.Events.Revision);
            app.Prescription.UpdateCoatingLayers(1, [], revision); Assert.Empty(app.Prescription.GetCoatingLayers(1));
            Assert.True(app.Documents.Undo()); Assert.Equal(2, app.Prescription.GetCoatingLayers(1).Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RemoveAllVariablesAlsoClearsCoatingFlagsIncludingImageSurfaceAndCanUndo()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var image = app.Prescription.GetSurfaces()[^1].Number;
        var layer = new CoatingLayerEditDto("N-BK7", 100, MultiplierVariable: true, IndexVariable: true, ExtinctionVariable: true);
        app.Prescription.UpdateCoatingLayers(1, [layer], app.Events.Revision);
        app.Prescription.UpdateCoatingLayers(image, [layer], app.Events.Revision);
        var revision = app.Events.Revision;
        app.Optimization.UpdateAllSurfaceVariables(OptimizationVariableUpdateMode.ClearAll);
        Assert.Equal(revision + 1, app.Events.Revision);
        foreach (var surface in new[] { 1, image })
        {
            var updated = Assert.Single(app.Prescription.GetCoatingLayers(surface));
            Assert.False(updated.MultiplierVariable); Assert.False(updated.IndexVariable); Assert.False(updated.ExtinctionVariable);
            Assert.Equal(100, updated.ThicknessNanometers);
        }
        Assert.True(app.Documents.Undo()); Assert.True(app.Prescription.GetCoatingLayers(image)[0].ExtinctionVariable);
    }

    internal static Optic CreateOptic()
    {
        var optic = new Optic("coating layer constraints");
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 2, MaterialAfter = new ConstantIndexMaterial("glass", 1.5), IsStop = true, SemiDiameter = 5,
                CoatingModel = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film A", 1.8, .05), 100, new(1.2, .15, .02)), new(new ConstantIndexMaterial("film B", 1.7, .05), 70, new(.7, -.1, -.01))]) },
            new OpticalSurface { Thickness = 5, SemiDiameter = 5, CoatingModel = new CoherentMultilayerCoating([new(new ConstantIndexMaterial("film C", 1.6), 85, new(1.8, .3, .1))]) },
            new OpticalSurface { SemiDiameter = 5 }]);
        optic.Fields.Add(new FieldPoint()); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true }); optic.Aperture.Value = 2;
        return optic;
    }
    private static MeritOperandDefinition Row(string code, int surface, int layer, double target = 0) => new()
    { Type = code, ZemaxIntegerParameters = [surface, layer], Weight = 1, Target = target };
    private static MeritOperandDefinition Coda(int data) => new()
    { Type = "CODA", ZemaxIntegerParameters = [1, 1], ZemaxDataParameters = [1, 0, 0, data], Weight = 1 };
    private static double Value(Optic optic, MeritOperandDefinition row)
    { var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); return result.Value; }
    private static void Near(double expected, double actual, double tolerance = 2e-11) => Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
}
