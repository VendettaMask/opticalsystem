using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class GaussianBeamOperandTests
{
    public static TheoryData<string> Codes => new("GBPD", "GBPP", "GBPR", "GBPS", "GBPW", "GBPZ");

    [Theory]
    [MemberData(nameof(Codes))]
    public void FreeSpaceMatchesAnalyticalGaussianBeam(string code)
    {
        var optic = Space(); var row = Row(code);
        var z = 30.0; var zr = Math.PI * .2 * .2 / .00055;
        var expected = code switch
        {
            "GBPD" => .00055 / (Math.PI * .2),
            "GBPP" => z,
            "GBPR" => z + zr * zr / z,
            "GBPS" => .2 * Math.Sqrt(1 + z * z / (zr * zr)),
            "GBPW" => .2,
            _ => zr
        };
        Value(optic, row, expected);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void ThinLensMatchesIndependentTwoRayMoments(string code)
    {
        var optic = Space(); optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        const double w0 = .2, distance = 10, length = 20, focal = 50;
        var theta = .00055 / (Math.PI * w0);
        var y1 = w0 * (1 - length / focal); var v1 = -w0 / focal;
        var y2 = theta * (distance + length * (1 - distance / focal)); var v2 = theta * (1 - distance / focal);
        var divergence = Math.Sqrt(v1 * v1 + v2 * v2);
        var size = Math.Sqrt(y1 * y1 + y2 * y2);
        var waist = Math.Abs(y1 * v2 - y2 * v1) / divergence;
        var dot = y1 * v1 + y2 * v2;
        var expected = code switch
        {
            "GBPD" => divergence,
            "GBPP" => dot / (divergence * divergence),
            "GBPR" => size * size / dot,
            "GBPS" => size,
            "GBPW" => waist,
            _ => waist / divergence
        };
        Value(optic, Row(code), expected);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void MSquaredScalesOnlySizeWaistAndDivergence(string code)
    {
        var optic = Space(); optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        var row = Row(code); var embedded = Evaluate(optic, row); row.ZemaxDataParameters[3] = 9;
        Value(optic, row, embedded * (code is "GBPD" or "GBPS" or "GBPW" ? 3 : 1));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void DielectricPlaneUsesInputAndOutputRefractiveIndices(string code)
    {
        var optic = Space(); optic.SurfaceGroup.Items[0].MaterialAfter = new ConstantIndexMaterial("Input", 1.2);
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Output", 1.8);
        var row = Row(code); row.ZemaxIntegerParameters[0] = 1;
        var z = 10 * 1.8 / 1.2; var zr = Math.PI * 1.8 * .2 * .2 / .00055;
        var expected = code switch
        {
            "GBPD" => .00055 / (Math.PI * 1.8 * .2),
            "GBPP" => z,
            "GBPR" => z + zr * zr / z,
            "GBPS" => .2 * Math.Sqrt(1 + z * z / (zr * zr)),
            "GBPW" => .2,
            _ => zr
        };
        Value(optic, row, expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void UseXChoosesTheEquivalentAxisForRotationallySymmetricSystem(int useX)
    {
        var row = Row("GBPS"); var reference = Evaluate(Space(), row);
        row.ZemaxDataParameters[0] = useX; Value(Space(), row, reference);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ZmxAndStaroptPreserveAllSlotsAndTargetWeight(string code)
    {
        var optic = OpticalFormatCatalog.Import(Source($"{code} 2 1 1 .2 -10 2.25 .625 2.5 0 0"), ".zmx");
        var row = Assert.Single(optic.MeritFunctionOperands);
        Assert.True(row.Enabled); Assert.False(row.CompatibilityOnly);
        Assert.Equal(new[] { 2, 1 }, row.ZemaxIntegerParameters);
        Assert.Equal(new[] { 1.0, .2, -10, 2.25 }, row.ZemaxDataParameters);
        var expected = Evaluate(optic, row); row.Enabled = false; row.CompatibilityOnly = true;
        var path = Path.Combine(Path.GetTempPath(), $"gaussian-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var loaded = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var restored = Assert.Single(loaded.MeritFunctionOperands);
            Assert.False(restored.CompatibilityOnly); Assert.False(restored.Enabled);
            Assert.Equal(row.ZemaxDataParameters, restored.ZemaxDataParameters);
            Assert.Equal(.625, restored.Target); Assert.Equal(2.5, restored.Weight);
            restored.Enabled = true; Value(loaded, restored, expected);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EditorUsesSixTypedSlotsAndExplainsM2(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 2, 0, 1, 0, 0, 0, 0, .6, 2, 0, 0, "",
            ZemaxInt1: 2, ZemaxInt2: 1, ZemaxData1: 1, ZemaxData2: .2, ZemaxData3: -10, ZemaxData4: 2.25)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction());
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Contains("sqrt(M²)", type.Calculation);
        Assert.Equal(6, type.Parameters!.Count); Assert.Equal("Flag", type.Parameters[2].ValueKind);
        Assert.Equal("Numeric", type.Parameters[5].ValueKind);
        var editor = new MeritOperandEditorRow(dto, type); Assert.Contains("M squared", editor.ParameterLabel(5));
        editor.Parameter6 = 4; app.Optimization.SetMeritFunction([editor.ToDto()]);
        var restored = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Equal(4, restored.ZemaxData4); Assert.Equal(-10, restored.ZemaxData3);
        Assert.Equal(.6, restored.Target); Assert.Equal(2, restored.Weight); Assert.Null(restored.ZemaxData5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 999)]
    [InlineData(1, -1)]
    [InlineData(1, 999)]
    public void BadSurfaceAndWavelengthReferencesFailAndDoNotUpgrade(int slot, int value)
    {
        var optic = Space(); var row = Row("GBPS"); row.ZemaxIntegerParameters[slot] = value;
        Invalid(optic, row); row.CompatibilityOnly = true; row.Enabled = false; optic.MeritFunctionOperands.Add(row);
        // Zero is a valid editing placeholder for surfaces, so only nonexistent references
        // are expected to remain compatibility-only at snapshot load.
        if (value != 0) Assert.True(Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands).CompatibilityOnly);
    }

    [Theory]
    [InlineData(0, .5)]
    [InlineData(0, double.NaN)]
    [InlineData(1, 0)]
    [InlineData(1, -.1)]
    [InlineData(1, double.NaN)]
    [InlineData(1, double.MaxValue)]
    [InlineData(2, double.NaN)]
    [InlineData(2, double.PositiveInfinity)]
    [InlineData(3, 0)]
    [InlineData(3, .5)]
    [InlineData(3, double.PositiveInfinity)]
    public void BadPhysicalParametersNeverBecomeSuccessfulZero(int slot, double value)
    {
        var row = Row("GBPS"); row.ZemaxDataParameters[slot] = value; Invalid(Space(), row);
    }

    [Fact]
    public void InputWaistPositionSignAndNoTrailingTranslationAreExplicit()
    {
        var optic = Space(); var row = Row("GBPP"); row.ZemaxIntegerParameters[0] = 1;
        row.ZemaxDataParameters[2] = 7; Value(optic, row, -7);
        row.ZemaxIntegerParameters[0] = 2; Value(optic, row, 13);
        optic.SurfaceGroup.Items[2].Thickness = 999; Value(optic, row, 13);
        optic.SurfaceGroup.Items[0].Thickness = 0; optic.SurfaceGroup.Renumber(); Value(optic, row, 13);
    }

    [Fact]
    public void PlaneWavefrontAtWaistHasInfiniteRadiusAndFiniteSize()
    {
        var optic = Space(); var row = Row("GBPR"); row.ZemaxIntegerParameters[0] = 1; row.ZemaxDataParameters[2] = 0;
        var data = optic.Paraxial.GaussianBeam(1, .55, .2, 0);
        Assert.True(double.IsPositiveInfinity(data.PhaseRadius)); Invalid(optic, row);
        row.Type = "GBPS"; Value(optic, row, .2);
    }

    [Fact]
    public void PrimaryWavelengthAndMaterialMutationAffectCurrentResult()
    {
        var optic = Space(); optic.Wavelengths[0].IsPrimary = false;
        optic.Wavelengths.Add(new Wavelength { Nanometers = 650, IsPrimary = true });
        var row = Row("GBPZ"); row.ZemaxIntegerParameters[1] = 0;
        Value(optic, row, Math.PI * .2 * .2 / .00065);
        row.ZemaxIntegerParameters[0] = 1;
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Glass", 2);
        Value(optic, row, 2 * Math.PI * .2 * .2 / .00065);
    }

    [Fact]
    public void AperturesDoNotClipTheParaxialEmbeddedMode()
    {
        var optic = Space(); var row = Row("GBPS"); var expected = Evaluate(optic, row);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.001, .001, 100, 100);
        Value(optic, row, expected);
    }

    [Theory]
    [InlineData("tilt")]
    [InlineData("decenter")]
    [InlineData("asphere")]
    [InlineData("reflection")]
    [InlineData("negative_gap")]
    [InlineData("stale_position")]
    public void UnsupportedOpticsAndStaleCoordinatesAreRejected(string kind)
    {
        var optic = Space(); var surface = optic.SurfaceGroup.Items[1];
        switch (kind)
        {
            case "tilt": surface.CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 0), 2, 0, 0); break;
            case "decenter": surface.CoordinateSystem = new CoordinateSystem(new Vector3D(1, 0, 0)); break;
            case "asphere": surface.Geometry = new EvenAsphereGeometry(50, 0, [0.001]); break;
            case "reflection": surface.IsReflective = true; break;
            case "negative_gap": surface.Thickness = -1; optic.SurfaceGroup.Renumber(); break;
            case "stale_position": surface.Thickness = 21; break;
        }
        Invalid(optic, Row("GBPS"));
    }

    [Fact]
    public void CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Space(), Row("GBPS")));
    }

    [Fact]
    public void FormalDlsCanMoveTheGaussianWaistToTheImageSurface()
    {
        var optic = Space(); optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = Row("GBPP"); row.Target = 0; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 20);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-6);
        Assert.InRange(Math.Abs(Evaluate(runtime.CurrentOptic, row)), 0, .001);
    }

    private static Optic Space()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), SemiDiameter = 10 },
            new OpticalSurface { Thickness = 20, Geometry = new PlaneGeometry(), SemiDiameter = 10, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), SemiDiameter = 10 }]);
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
    private static string Source(string row) => $"""
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
        {row}
        """;
    private static MeritOperandDefinition Row(string code) => new()
    {
        Type = code,
        Surface = 2,
        Wavelength = 1,
        Target = .6,
        Weight = 2,
        ZemaxIntegerParameters = [2, 1],
        ZemaxDataParameters = [0, .2, -10, 1]
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(row.Weight * Math.Pow(result.Value - row.Target, 2), result.Contribution, 8);
        return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected) => Assert.Equal(expected, Evaluate(optic, row), 8);
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
