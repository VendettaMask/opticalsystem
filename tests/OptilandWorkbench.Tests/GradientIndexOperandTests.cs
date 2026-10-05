using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class GradientIndexOperandTests
{
    public static IEnumerable<object[]> ProfilePoints => from model in Enumerable.Range(1, 4)
                                                         from point in Enumerable.Range(1, 6)
                                                         select new object[] { model, point };
    public static IEnumerable<object[]> Codes => new[] { "DLTN", "GRMN", "GRMX" }
        .Concat(from point in Enumerable.Range(1, 6) from suffix in new[] { "GT", "LT", "VA" } select $"I{point}{suffix}")
        .Select(code => new object[] { code });

    [Theory]
    [MemberData(nameof(ProfilePoints))]
    public void AllSixPositionsReadTheFormalSpatialFieldWithIndependentPolynomialValues(int model, int point)
    {
        var optic = Lens(Profile(model));
        var front = optic.SurfaceGroup.Items[1]; var back = optic.SurfaceGroup.Items[2];
        front.Geometry = new StandardGeometry(20); back.Geometry = new StandardGeometry(-30);
        var x = point is 3 or 6 ? 4.0 : 0; var y = point is 2 or 5 ? 4.0 : 0;
        var z = point <= 3 ? 20 - Math.Sqrt(400 - x * x - y * y) : 8 - 30 + Math.Sqrt(900 - x * x - y * y);
        var expected = IndependentIndex(model, x, y, z);
        var control = GradientIndexControlMetrics.AtPoint(front, back, point, 550);
        Assert.Equal(x, control.LocalPosition.X); Assert.Equal(y, control.LocalPosition.Y);
        Assert.Equal(z, control.LocalPosition.Z, 12); Assert.Equal(expected, control.Index, 12);
        Assert.Equal(expected, Value(optic, Row($"I{point}VA")), 12);
        // Neither finite-NA trace requirements nor the scalar paraxial guard can change material constraints.
        if (model is 1 or 4) Assert.Throws<NotSupportedException>(() => optic.Paraxial.EstimateEffectiveFocalLength());
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void BoundsUseTheSpecifiedTargetAndAllRawSlotsSurviveNativePreservation(string code)
    {
        var optic = Lens(Profile(4));
        var indices = new[] { 1.5, 1.5 + .02 * 4 + .003 * 16, 1.5 + .01 * 4 + .002 * 16,
            1.5 + .004 * 8 + .0005 * 64, 1.5 + .02 * 4 + .003 * 16 + .004 * 8 + .0005 * 64,
            1.5 + .01 * 4 + .002 * 16 + .004 * 8 + .0005 * 64 };
        var raw = code switch { "DLTN" => .064, "GRMN" => indices.Min(), "GRMX" => indices.Max(), _ => indices[code[1] - '1'] };
        foreach (var target in new[] { raw - .1, raw, raw + .1 })
        {
            var row = Row(code); row.Target = target; row.Weight = -3;
            var expected = code == "GRMN" || code.EndsWith("GT", StringComparison.Ordinal) ? Math.Min(raw, target)
                : code == "GRMX" || code.EndsWith("LT", StringComparison.Ordinal) ? Math.Max(raw, target) : raw;
            var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error);
            Assert.Equal(expected, result.Value, 12); Assert.Equal(3 * Math.Pow(expected - target, 2), result.Contribution, 12);
        }
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX"));
        var imported = OpticalFormatCatalog.Import(source + $"\n{code} 1 2 11 12 13 14 1.6 3 0 0\n", ".zmx");
        var original = imported.MeritFunctionOperands.Last();
        Assert.True(original.CompatibilityOnly); Assert.False(original.Enabled);
        Assert.Equal(new[] { 1, 2 }, original.ZemaxIntegerParameters);
        Assert.Equal(new[] { 11.0, 12, 13, 14 }, original.ZemaxDataParameters);
        var restored = Optic.FromSnapshot(imported.ToSnapshot()).MeritFunctionOperands.Last();
        Assert.True(restored.CompatibilityOnly); Assert.False(restored.Enabled);
        Assert.Equal(original.ZemaxDataParameters, restored.ZemaxDataParameters);
        restored.Enabled = true;
        var failure = MeritFunctionCatalog.Evaluate(optic, restored);
        Assert.NotEmpty(failure.Error); Assert.True(double.IsPositiveInfinity(failure.Contribution));
    }

    [Theory]
    [InlineData(-20, 30)]
    [InlineData(20, -30)]
    [InlineData(-20, -30)]
    [InlineData(20, 30)]
    public void BlankDeltaUsesBothSagExtentsAndEachEndsOwnClearRadius(double r1, double r2)
    {
        var optic = Lens(new Gradient3IndexProfile(1.5, axial1: -.01, axial2: .0002));
        var front = optic.SurfaceGroup.Items[1]; var back = optic.SurfaceGroup.Items[2];
        front.Geometry = new StandardGeometry(r1); back.Geometry = new StandardGeometry(r2);
        var a = Math.Min(0, Math.CopySign(20 - Math.Sqrt(400 - 9), r1));
        var b = 8 + Math.Max(0, Math.CopySign(30 - Math.Sqrt(900 - 16), r2));
        var blank = GradientIndexControlMetrics.AxialBlank(front, back, 550);
        Assert.Equal(a, blank.MinimumZ, 12); Assert.Equal(b, blank.MaximumZ, 12);
        Assert.Equal(Math.Abs(-.01 * (b - a) + .0002 * (b * b - a * a)), Value(optic, Row("DLTN")), 12);
        front.MechanicalSemiDiameter = 40; front.ChipZone = 3; back.MechanicalSemiDiameter = 50;
        Assert.Equal(blank, GradientIndexControlMetrics.AxialBlank(front, back, 550));
    }

    [Fact]
    public void DeltaIsEndpointDifferenceAndSixPointBoundsDoNotClaimVolumeExtrema()
    {
        var optic = Lens(new Gradient3IndexProfile(1.5, axial1: .08, axial2: -.01));
        Assert.Equal(0, Value(optic, Row("DLTN")), 12);
        var lower = Row("GRMN"); lower.Target = 2;
        var upper = Row("GRMX"); upper.Target = 0;
        Assert.Equal(1.5, Value(optic, lower), 12); Assert.Equal(1.5, Value(optic, upper), 12);
        Assert.Equal(1.66, ((GradientIndexMaterial)optic.SurfaceGroup.Items[1].MaterialAfter).RefractiveIndex(new(0, 0, 4), 550), 12);
    }

    [Fact]
    public void CommonRigidPoseDoesNotChangeEntranceLocalControlPoints()
    {
        var optic = Lens(Profile(4)); var front = optic.SurfaceGroup.Items[1]; var back = optic.SurfaceGroup.Items[2];
        var before = GradientIndexControlMetrics.SixPoints(front, back, 550);
        var frame = new CoordinateSystem(new(12, -30, 45), 20, -30, 42);
        front.CoordinateSystem = frame;
        back.CoordinateSystem = frame with { Origin = frame.ToGlobalPoint(new(0, 0, 8)) };
        Assert.Equal(before, GradientIndexControlMetrics.SixPoints(front, back, 550));
    }

    [Fact]
    public void IndividualPointsDoNotInventGradientsOrRequireUnrequestedPointsToBeValid()
    {
        var optic = Lens(new Gradient1IndexProfile(1.5, -.2, .01));
        Assert.Equal(1.5, Value(optic, Row("I1VA")), 12);
        var result = MeritFunctionCatalog.Evaluate(optic, Row("GRMN"));
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(-2.0)]
    public void ConicBoundaryAndMaterialEditsImmediatelyChangeTheControlValue(double conic)
    {
        var optic = Lens(new Gradient3IndexProfile(1.5, axial1: .01));
        var front = optic.SurfaceGroup.Items[1]; var back = optic.SurfaceGroup.Items[2];
        front.Geometry = new StandardGeometry(20, conic);
        double Sag(double r) => r * r / (20 * (1 + Math.Sqrt(1 - (1 + conic) * r * r / 400)));
        Assert.Equal(1.5 + .01 * Sag(4), Value(optic, Row("I2VA")), 12);
        back.SemiDiameter = 2; Assert.Equal(1.5 + .01 * Sag(3), Value(optic, Row("I2VA")), 12);
        front.MaterialAfter = new GradientIndexMaterial("updated", new Gradient3IndexProfile(1.6, axial1: .02), 100);
        Assert.Equal(1.6 + .02 * Sag(3), Value(optic, Row("I2VA")), 12);
        Assert.Equal(Value(optic, Row("I2VA")), Value(Optic.FromSnapshot(optic.ToSnapshot()), Row("I2VA")), 12);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("image")]
    [InlineData("missing-surface")]
    [InlineData("wave-zero")]
    [InlineData("wave-range")]
    [InlineData("homogeneous")]
    [InlineData("negative-thickness")]
    [InlineData("unsynchronized")]
    [InlineData("relative-tilt")]
    [InlineData("asphere")]
    [InlineData("sag-domain")]
    [InlineData("nonfinite-pose")]
    public void InvalidAndUnsupportedRequestsFailInsteadOfBecomingSuccessfulZero(string problem)
    {
        var optic = Lens(Profile(3)); var row = Row("I2VA"); var front = optic.SurfaceGroup.Items[1]; var back = optic.SurfaceGroup.Items[2];
        switch (problem)
        {
            case "object": row.ZemaxIntegerParameters[0] = 0; break;
            case "image": row.ZemaxIntegerParameters[0] = 3; break;
            case "missing-surface": row.ZemaxIntegerParameters[0] = 999; break;
            case "wave-zero": row.ZemaxIntegerParameters[1] = 0; break;
            case "wave-range": row.ZemaxIntegerParameters[1] = 9; break;
            case "homogeneous": front.MaterialAfter = new ConstantIndexMaterial("glass", 1.5); break;
            case "negative-thickness": front.Thickness = -1; break;
            case "unsynchronized": back.CoordinateSystem = new(new(1, 0, 8)); break;
            case "relative-tilt": back.CoordinateSystem = new(new(0, 0, 8), 1); break;
            case "asphere": front.Geometry = new EvenAsphereGeometry(40, 0, [.001]); break;
            case "sag-domain": front.Geometry = new StandardGeometry(2); break;
            default: front.CoordinateSystem = new(new(double.NaN, 0, 0)); break;
        }
        var result = MeritFunctionCatalog.Evaluate(optic, row);
        Assert.NotEmpty(result.Error); Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
        Assert.ThrowsAny<Exception>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, [row]));
    }

    [Fact]
    public void RawParametersAreAuthoritativeAndCancellationEscapesTheEvaluator()
    {
        var optic = Lens(Profile(3)); var row = Row("I4VA"); row.Surface = 999; row.Wavelength = 999;
        Assert.Equal(IndependentIndex(3, 0, 0, 8), Value(optic, row), 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => GradientIndexControlMetrics.AtPoint(optic.SurfaceGroup.Items[1], optic.SurfaceGroup.Items[2], 7, 550));
        Assert.Throws<ArgumentOutOfRangeException>(() => GradientIndexControlMetrics.AtPoint(optic.SurfaceGroup.Items[1], optic.SurfaceGroup.Items[2], 1, 0));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
    }

    [Fact]
    public async Task ApplicationEditingAndStarOptPreserveAllTwentyOneExecutableRowsAndTheirParameterSemantics()
    {
        var optic = Lens(Profile(3));
        foreach (var code in Codes.Select(c => (string)c[0])) optic.MeritFunctionOperands.Add(Row(code));
        var path = Path.Combine(Path.GetTempPath(), $"grin-operands-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            var types = app.Optimization.GetMeritOperandTypes(); var rows = app.Optimization.GetMeritFunction(); Assert.Equal(21, rows.Count);
            foreach (var row in rows)
            {
                Assert.Empty(row.Error); var type = types.Single(t => t.Code == row.Type);
                Assert.False(type.CompatibilityOnly); Assert.Equal(6, type.Parameters!.Count);
                Assert.Contains("原生", type.Calculation); Assert.Contains("GRIN", type.Category);
                var editor = new MeritOperandEditorRow(row, type);
                Assert.True(editor.IsParameterEditable(0)); Assert.True(editor.IsParameterEditable(1)); Assert.False(editor.IsParameterEditable(2));
                editor.Parameter2 = 2; editor.Target = 1.6;
                var update = editor.ToDto();
                rows = rows.Select(r => r.Index == row.Index ? update : r).ToArray();
            }
            app.Optimization.SetMeritFunction(rows); await app.Documents.SaveAsync(path);
            var copy = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            Assert.Equal(21, copy.MeritFunctionOperands.Count);
            foreach (var row in copy.MeritFunctionOperands)
            {
                Assert.False(row.CompatibilityOnly); Assert.Equal(2, row.ZemaxIntegerParameters[1]); Assert.Equal(1.6, row.Target);
                Assert.Empty(MeritFunctionCatalog.Evaluate(copy, row).Error);
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("I4VA", 1.56)]
    [InlineData("DLTN", .06)]
    public void ProductionDlsOptimizesActualGrinThicknessAgainstMaterialConstraints(string code, double target)
    {
        var optic = Lens(new Gradient3IndexProfile(1.5, axial1: .01));
        optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = Row(code); row.Target = target; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-8);
        Assert.InRange(Math.Abs(runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness - 6), 0, 1e-5);
    }

    private static Optic Lens(ISpatialRefractiveIndex profile)
    {
        var material = new GradientIndexMaterial("GRIN", profile, 100);
        var optic = new Optic("GRIN material controls");
        optic.Fields.Add(new FieldPoint { Y = 0 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 650, IsPrimary = false });
        optic.Aperture.Kind = ApertureKind.FloatByStopSize;
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { MaterialAfter = material, Thickness = 8, SemiDiameter = 3, SemiDiameterFixed = true },
            new OpticalSurface { MaterialBefore = material, Thickness = 5, SemiDiameter = 4, SemiDiameterFixed = true, IsStop = true },
            new OpticalSurface { Thickness = 0 }
        ]);
        return optic;
    }
    private static MeritOperandDefinition Row(string code) => new()
    { Type = code, Surface = 1, Wavelength = 1, Enabled = true, Weight = 1, ZemaxIntegerParameters = [1, 1], ZemaxDataParameters = [0, 0, 0, 0] };
    private static double Value(Optic optic, MeritOperandDefinition row)
    { var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); return result.Value; }
    private static ISpatialRefractiveIndex Profile(int model) => model switch
    {
        1 => new Gradient1IndexProfile(1.5, .002, .01),
        2 => new Gradient2IndexProfile(2.25, .001, .00001, .000001, .00000001, .000000001, .00000000001),
        3 => new Gradient3IndexProfile(1.5, .001, .00001, .000001, .004, .0005, .00001),
        _ => new Gradient4IndexProfile(1.5, .01, .002, .02, .003, .004, .0005)
    };
    private static double IndependentIndex(int model, double x, double y, double z)
    {
        var r2 = x * x + y * y;
        return model switch
        {
            1 => 1.5 + .002 * r2 + .01 * Math.Sqrt(r2),
            2 => Math.Sqrt(2.25 + .001 * r2 + .00001 * Math.Pow(r2, 2) + .000001 * Math.Pow(r2, 3)
                + .00000001 * Math.Pow(r2, 4) + .000000001 * Math.Pow(r2, 5) + .00000000001 * Math.Pow(r2, 6)),
            3 => 1.5 + .001 * r2 + .00001 * Math.Pow(r2, 2) + .000001 * Math.Pow(r2, 3) + .004 * z + .0005 * z * z + .00001 * z * z * z,
            _ => 1.5 + .01 * x + .002 * x * x + .02 * y + .003 * y * y + .004 * z + .0005 * z * z
        };
    }
}
