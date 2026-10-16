using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class IlluminationInverseNativeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("123456", "EFNO")]
    [InlineData("123456", "RELI")]
    [InlineData("double-gauss-28-degree-field", "EFNO")]
    [InlineData("double-gauss-28-degree-field", "RELI")]
    [InlineData("relay-lens", "EFNO")]
    [InlineData("relay-lens", "RELI")]
    [InlineData("even-asphere", "EFNO")]
    [InlineData("even-asphere", "RELI")]
    public void OriginalFourFileOperandsAgreeAtSharedRayTracePrecision(string name, string code)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "validation/zemax/2026-r1/fcgt-efno-2026-10-14/manifest.json")));
        var item = manifest.RootElement.GetProperty("Cases").EnumerateArray().Single(row => row.GetProperty("Name").GetString() == name);
        var path = Path.Combine(root, item.GetProperty("Source").GetString()!);
        Assert.Equal(item.GetProperty("SourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(path), ".zmx");
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, item.GetProperty("Capture").GetString()!, "operands.json")));
        double maximum = 0;
        var rows = capture.RootElement.EnumerateArray().Where(row => row.GetProperty("code").GetString() == code).ToArray();
        foreach (var row in rows)
        {
            var value = MeritFunctionCatalog.Evaluate(optic, Row(code, row.GetProperty("density").GetInt32(),
                row.GetProperty("wave").GetInt32(), row.GetProperty("field").GetInt32()));
            Assert.Empty(value.Error);
            var expected = row.GetProperty("value").GetDouble();
            maximum = Math.Max(maximum, code == "EFNO" ? Math.Abs(value.Value / expected - 1) : Math.Abs(value.Value - expected));
        }
        // A new identity regression for this captured profile, covering all 256
        // rows: relative EFNO / absolute RELI at 1e-8. This numerical guard allows
        // the existing native-vs-Core aiming trace floor; it changes neither the
        // frozen references nor any old acceptance target, and is not a global
        // commercial-accuracy specification.
        Assert.InRange(maximum, 0, 1e-8);
        output.WriteLine($"{name}/{code}: {rows.Length} original rows, maximum={maximum:R}");
    }

    [Theory]
    [InlineData(-4, -9)]
    [InlineData(4, -9)]
    [InlineData(0, -8)]
    [InlineData(-9, 0)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(9, 0)]
    [InlineData(0, 8)]
    [InlineData(-4, 9)]
    [InlineData(4, 9)]
    public void NativeLocatedNodeMaskRemovesTheSameFiniteContribution(int column, int row)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "validation/zemax/2026-r1/illumination-inverse-2026-10-16/controls.json")));
        var path = Path.Combine(root, fixture.RootElement.GetProperty("source").GetString()!);
        Assert.Equal(fixture.RootElement.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        var capture = fixture.RootElement.GetProperty("nodes").EnumerateArray().Single(node =>
            node.GetProperty("column").GetInt32() == column && node.GetProperty("row").GetInt32() == row);
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(path), ".zmx");
        var stop = optic.SurfaceGroup.Items[1];
        var distance = stop.Thickness; stop.Thickness = 0;
        var mask = new OpticalSurface { Thickness = distance, SemiDiameter = 20 };
        optic.SurfaceGroup.Items.Insert(2, mask);
        var baseline = IlluminationMetrics.Evaluate(optic, (0, 1), .55, 10,
            sampling: IlluminationSamplingKind.FiniteImageCosine, includeScalarTransmission: false).EffectiveFNumber;
        Assert.InRange(Math.Abs(baseline / capture.GetProperty("baseline").GetDouble() - 1), 0, 1e-9);
        var bounds = capture.GetProperty("bounds").EnumerateArray().ToArray();
        var x = 5 * (bounds[0].GetProperty("low").GetDouble() + bounds[0].GetProperty("high").GetDouble());
        var y = 5 * (bounds[1].GetProperty("low").GetDouble() + bounds[1].GetProperty("high").GetDouble());
        mask.PhysicalAperture = new DifferenceAperture(new CircularAperture(20),
            new RectangularAperture(.001, .001, centerX: x, centerY: y) { HalfWidth = 2e-6, HalfHeight = 2e-6 });
        var blocked = IlluminationMetrics.Evaluate(optic, (0, 1), .55, 10,
            sampling: IlluminationSamplingKind.FiniteImageCosine, includeScalarTransmission: false).EffectiveFNumber;
        Assert.True(blocked > baseline);
        Assert.InRange(Math.Abs(blocked / capture.GetProperty("maskedValue").GetDouble() - 1), 0, 1e-9);
    }

    [Fact]
    public void ExplicitSvdCutoffIsRelativeAndDoesNotChangeTheGeneralSolverDefault()
    {
        double[,] design = { { 1, 0, 0 }, { 0, 1.001e-7, 0 }, { 0, 0, .999e-7 } };
        double[] target = [2, 3 * 1.001e-7, 4 * .999e-7];
        var finite = JacobiSvd.Solve(JacobiSvd.Factor(design, 1e-7), target);
        Assert.Equal(2, finite[0], 12); Assert.Equal(3, finite[1], 12); Assert.Equal(0, finite[2]);
        var general = JacobiSvd.Solve(JacobiSvd.Factor(design), target);
        Assert.Equal(2, general[0], 12); Assert.Equal(3, general[1], 12); Assert.Equal(4, general[2], 12);
        var scaled = (double[,])design.Clone();
        for (var row = 0; row < 3; row++) for (var column = 0; column < 3; column++) scaled[row, column] *= 100;
        var rescaled = JacobiSvd.Solve(JacobiSvd.Factor(scaled, 1e-7), target.Select(value => value * 100).ToArray());
        for (var index = 0; index < finite.Length; index++) Assert.Equal(finite[index], rescaled[index], 12);
    }

    [Fact]
    public void SharedSvdRejectsNonFiniteDataAndHonorsCancellation()
    {
        Assert.Throws<InvalidOperationException>(() => JacobiSvd.Factor(new double[,] { { double.NaN } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => JacobiSvd.Factor(new double[,] { { 1 } }, 1));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var scope = ComputationCancellation.Push(cancel.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => JacobiSvd.Factor(new double[,] { { 1 } }));
    }

    private static MeritOperandDefinition Row(string code, int density, int wave, int field) => new()
    {
        Type = code,
        ZemaxIntegerParameters = [density, wave],
        ZemaxDataParameters = [field, 0, 0, 0]
    };

}
