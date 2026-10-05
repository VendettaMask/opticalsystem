using System.Globalization;
using System.Text.RegularExpressions;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class BestFitSphereOperandTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(40, false)]
    [InlineData(-40, false)]
    [InlineData(40, true)]
    [InlineData(-40, true)]
    public void SphericalSurfaceHasExactRadiusAndNoMaterialToRemove(double radius, bool exit)
    {
        var surface = Surface(new StandardGeometry(radius));
        if (exit) (surface.MaterialBefore, surface.MaterialAfter) = (surface.MaterialAfter, surface.MaterialBefore);
        var fit = SurfaceBestFitSphere.MinimumVolume(surface);
        Assert.Equal(1 / radius, fit.Curvature, 14); Assert.Equal(radius, fit.Value(1), 12);
        foreach (var data in Enumerable.Range(2, 6)) Assert.InRange(Math.Abs(fit.Value(data)), 0, 1e-12);
    }

    [Fact]
    public void PlaneCurvatureIsZeroButInfiniteRadiusCannotBeASuccessfulMeritValue()
    {
        var optic = Lens(new PlaneGeometry());
        Assert.Equal(0, Value(optic, Row(0))); Invalid(optic, Row(1));
        foreach (var data in Enumerable.Range(2, 6)) Assert.Equal(0, Value(optic, Row(data)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsphereFitEnclosesAirSideAndBeatsTheBaseSphereVolume(bool exit)
    {
        var surface = Surface(new EvenAsphereGeometry(double.PositiveInfinity, 0, [0, .00002]));
        if (exit) (surface.MaterialBefore, surface.MaterialAfter) = (surface.MaterialAfter, surface.MaterialBefore);
        var fit = SurfaceBestFitSphere.MinimumVolume(surface);
        var side = exit ? 1 : -1;
        const int n = 20000; const double rmax = 5;
        var volume = 0.0; var min = double.PositiveInfinity; var max = 0.0;
        for (var i = 0; i <= n; i++)
        {
            var r = rmax * i / n;
            // Independent analytic sphere and quartic surface; no runtime metric supplies the reference.
            var radius = 1 / fit.Curvature;
            var sphere = radius - Math.CopySign(Math.Sqrt(radius * radius - r * r), radius);
            var depth = side * (sphere + fit.VertexOffset - .00002 * Math.Pow(r, 4));
            min = Math.Min(min, depth); max = Math.Max(max, depth);
            volume += (i is 0 or n ? .5 : 1) * 2 * Math.PI * r * depth * rmax / n;
        }
        Assert.InRange(min, -1e-9, 1e-7); Assert.Equal(max, fit.MaximumRemoval, 8);
        Assert.Equal(volume, fit.RemovalVolume, 7);
        var baseVolume = exit ? Math.PI * .00002 * 2 * Math.Pow(rmax, 6) / 3
            : Math.PI * .00002 * Math.Pow(rmax, 6) / 3;
        Assert.True(fit.RemovalVolume < .7 * baseVolume);
        Assert.True(fit.RmsRemoval > 0 && fit.RmsRemoval <= fit.MaximumRemoval);
        Assert.True(fit.RmsSlopeDifference > 0 && fit.RmsSlopeDifference <= fit.MaximumSlopeDifference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignAndMaterialReversalPreserveRemovalAndReverseSphere(bool odd)
    {
        IGeometry Shape(double sign) => odd
            ? new OddAsphereGeometry(sign * 30, -.5, [0, sign * .0001, sign * -.00002])
            : new EvenAsphereGeometry(sign * 30, -.5, [0, sign * .00002]);
        var a = Surface(Shape(1)); var b = Surface(Shape(-1));
        (b.MaterialBefore, b.MaterialAfter) = (b.MaterialAfter, b.MaterialBefore);
        var x = SurfaceBestFitSphere.MinimumVolume(a, 1, 5);
        var y = SurfaceBestFitSphere.MinimumVolume(b, 1, 5);
        Assert.Equal(-x.Curvature, y.Curvature, 9); Assert.Equal(-x.VertexOffset, y.VertexOffset, 8);
        Assert.Equal(x.RemovalVolume, y.RemovalVolume, 8); Assert.Equal(x.MaximumRemoval, y.MaximumRemoval, 8);
        Assert.Equal(x.RmsSlopeDifference, y.RmsSlopeDifference, 8);
    }

    [Fact]
    public void DimensionalScalingAndAnnularRangeAreRespected()
    {
        var a = Surface(new EvenAsphereGeometry(30, -.5, [0, .00002]));
        var b = Surface(new EvenAsphereGeometry(90, -.5, [0, .00002 / 27])); b.SemiDiameter = 15;
        var x = SurfaceBestFitSphere.MinimumVolume(a, 1, 5);
        var y = SurfaceBestFitSphere.MinimumVolume(b, 3, 15);
        Assert.Equal(x.Curvature / 3, y.Curvature, 8); Assert.Equal(x.VertexOffset * 3, y.VertexOffset, 7);
        Assert.Equal(x.RemovalVolume * 27, y.RemovalVolume, 7);
        Assert.Equal(x.MaximumRemoval * 3, y.MaximumRemoval, 7);
        Assert.Equal(x.RmsRemoval * 3, y.RmsRemoval, 7); Assert.Equal(x.RmsSlopeDifference, y.RmsSlopeDifference, 8);
        var full = SurfaceBestFitSphere.MinimumVolume(a);
        Assert.True(Math.Abs(full.Curvature - x.Curvature) > 1e-6);
        Assert.Equal(full, SurfaceBestFitSphere.MinimumVolume(a, 0, 5));
    }

    [Fact]
    public void CommittedSphericalSagTableProvidesAFileSpecificReference()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(directory, "zemax-123456.ZMX")), ".zmx");
        var text = File.ReadAllText(Path.Combine(directory, "zemax-123456-sag-table.txt"));
        double Read(string heading) => double.Parse(Regex.Match(text, Regex.Escape(heading) + @"\s*:\s*([-+]?\d+(?:\.\d+)?(?:E[-+]?\d+)?)").Groups[1].Value, CultureInfo.InvariantCulture);
        var nativeRadius = Read("最佳拟合球面半径");
        var nativeOffset = Read("最佳拟合球面顶点偏移");
        var radius = Value(optic, Row(1)); var offset = Value(optic, Row(2));
        Assert.InRange(Math.Abs(radius - nativeRadius), 0, 5e-6); Assert.Equal(nativeOffset, offset, 12);
        foreach (var data in Enumerable.Range(3, 5)) Assert.InRange(Math.Abs(Value(optic, Row(data))), 0, 1e-10);
        output.WriteLine($"BFSD captured spherical sag table: current radius={radius:R}, captured rounded radius={nativeRadius:R}, difference={radius - nativeRadius:R} mm. Surface=1; not a native MFE or asphere-fit capture.");
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    [InlineData(1, 0)]
    [InlineData(double.NaN, 5)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(0, 41)]
    public void InvalidRangeNeverProducesSuccessfulZero(double min, double max) => Invalid(Lens(new StandardGeometry(40)), Row(0, min, max));

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public void InvalidDataNeverProducesSuccessfulZero(int data) => Invalid(Lens(new StandardGeometry(40)), Row(data));

    [Theory]
    [InlineData("mirror")]
    [InlineData("air")]
    [InlineData("glass")]
    public void AmbiguousMaterialSideIsNotGuessed(string kind)
    {
        var optic = Lens(new StandardGeometry(40)); var surface = optic.SurfaceGroup.Items[1];
        if (kind == "mirror") surface.IsReflective = true;
        else if (kind == "air") surface.MaterialAfter = new AirMaterial();
        else surface.MaterialBefore = new ConstantIndexMaterial("Other glass", 1.7);
        Invalid(optic, Row(0));
    }

    [Fact]
    public void CancellationPropagatesAndRawSlotsIgnoreUnrelatedAliases()
    {
        var optic = Lens(new StandardGeometry(40)); var row = Row(0);
        row.Surface = 999; row.Wavelength = 999; row.Hx = 999; row.Hy = 999;
        Assert.Equal(.025, Value(optic, row), 12);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var scope = ComputationCancellation.Push(cancel.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
    }

    [Fact]
    public async Task EditorAndStarOptPreserveLocalSlotsWhileNativeRowsRemainReadOnly()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, "BFSD", 1, 0, 0, 0, 0, 0, 0, .02, 2, 0, 0, "BFS",
            ZemaxInt1: 1, ZemaxInt2: 0, ZemaxData1: 1, ZemaxData2: 5, ZemaxData3: 12, ZemaxData4: -3)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(dto.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == "BFSD"); Assert.False(type.CompatibilityOnly);
        var editor = new MeritOperandEditorRow(dto, type); Assert.False(editor.IsParameterEditable(4));
        editor.Parameter2 = 1; editor.Parameter4 = 6; app.Optimization.SetMeritFunction([editor.ToDto()]);
        var changed = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(changed.Error);
        Assert.Equal(1, changed.ZemaxInt2); Assert.Equal(6, changed.ZemaxData2); Assert.Equal(12, changed.ZemaxData3);
        var optic = Lens(new StandardGeometry(40)); optic.MeritFunctionOperands.Add(Row(0));
        var native = OpticalFormatCatalog.Import("MODE SEQ\nENPD 10\nWAVM 1 0.55 1\nSURF 0\nDISZ 10\nSURF 1\nSTOP\nDISZ 1\nSURF 2\nBFSD 1 0 0 5 0 0 0 1 0 0\n", ".zmx");
        optic.MeritFunctionOperands.Add(Assert.Single(native.MeritFunctionOperands));
        var path = Path.Combine(Path.GetTempPath(), $"bfs-operand-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            Assert.False(restored.MeritFunctionOperands[0].CompatibilityOnly);
            Assert.Equal(.025, Value(restored, restored.MeritFunctionOperands[0]), 12);
            var imported = restored.MeritFunctionOperands[1]; Assert.True(imported.CompatibilityOnly); Assert.False(imported.Enabled);
            Assert.Equal(optic.MeritFunctionOperands[1].Comment, imported.Comment); imported.Enabled = true; Invalid(restored, imported);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BfsCurvatureDrivesProductionDls()
    {
        var optic = Lens(new StandardGeometry(40)); optic.SurfaceGroup.Items[1].RadiusVariable = true;
        var row = Row(0); row.Target = .02; row.Weight = 10; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-6);
        Assert.Equal(50, runtime.CurrentOptic.SurfaceGroup.Items[1].Radius, 4);
    }

    private static OpticalSurface Surface(IGeometry geometry) => new()
    {
        Geometry = geometry,
        SemiDiameter = 5,
        MaterialBefore = new AirMaterial(),
        MaterialAfter = new ConstantIndexMaterial("Glass", 1.5)
    };
    private static Optic Lens(IGeometry geometry)
    {
        var optic = Optic.CreateBlank(); var front = Surface(geometry); front.Thickness = 10; front.IsStop = true;
        optic.SurfaceGroup.Replace([new OpticalSurface { Thickness = double.PositiveInfinity }, front, new OpticalSurface { Thickness = 0 }]);
        return optic;
    }
    private static MeritOperandDefinition Row(int data, double min = 0, double max = 0) => new()
    {
        Type = "BFSD",
        ZemaxIntegerParameters = [1, data],
        ZemaxDataParameters = [min, max, 0, 0]
    };
    private static double Value(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }
}
