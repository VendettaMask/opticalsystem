using System.Globalization;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class SeidelOperandTests
{
    public static TheoryData<string> NewCodes => new("SPHA", "COMA", "ASTI", "FCUR", "PETC");
    public static TheoryData<string> WaveCodes => new("SPHA", "COMA", "ASTI", "FCUR");

    [Theory]
    [MemberData(nameof(NewCodes))]
    public async Task ImportAndNativeSaveKeepRawSlotsAndState(string code)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 0.5 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              CURV 0.01
              DISZ 50
            SURF 2
              DISZ 0
            {code} 1 1 0.125 -0.25 0.375 -0.5 0.625 2.5 0 0
            """, ".zmx");
        var path = Path.Combine(Path.GetTempPath(), $"seidel-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var row = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(code, row.Type); Assert.Equal(new[] { 1, 1 }, row.ZemaxIntegerParameters);
            Assert.Equal(new[] { .125, -.25, .375, -.5 }, row.ZemaxDataParameters);
            Assert.Equal(.625, row.Target); Assert.Equal(2.5, row.Weight);
            Assert.True(row.Enabled); Assert.False(row.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void OldCompatibilityRowsUpgradeWithoutEnabling(string code)
    {
        var optic = Lens(); var row = Row(code, 1, 1); row.CompatibilityOnly = true; row.Enabled = false;
        optic.MeritFunctionOperands.Add(row);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.False(restored.CompatibilityOnly); Assert.False(restored.Enabled);
    }

    [Theory]
    [InlineData("SPHA", 100)]
    [InlineData("COMA", 100)]
    [InlineData("ASTI", 100)]
    [InlineData("FCUR", 100)]
    [InlineData("SPHA", -100)]
    [InlineData("COMA", -100)]
    [InlineData("ASTI", -100)]
    [InlineData("FCUR", -100)]
    public void SingleSphericalInterfaceMatchesIndependentThirdOrderFormula(string code, double radius)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Radius = radius;
        const double n = 1.5, y = 5, lambda = .0005;
        var t = Math.Tan(5 * Math.PI / 180);
        var expected = code switch
        {
            "SPHA" => (n - 1) * Math.Pow(y, 4) / (n * n * Math.Pow(radius, 3) * 8 * lambda),
            "COMA" => (n - 1) * Math.Pow(y, 3) * t / (n * n * radius * radius * 2 * lambda),
            "ASTI" => (n - 1) * y * y * t * t / (n * n * radius * 2 * lambda),
            _ => (n - 1) * y * y * t * t / (n * radius * 4 * lambda)
        };
        Value(optic, Row(code, 1, 1), expected); Value(optic, Row(code, 0, 1), expected);
        Value(optic, Row(code, 2, 1), 0);
    }

    [Theory]
    [MemberData(nameof(WaveCodes))]
    public void WholeSystemEqualsSurfaceSumAndKeepsWavelengthUnits(string code)
    {
        var optic = Optic.CreateCookeTriplet();
        var total = Evaluate(optic, Row(code, 0, 1));
        var sum = optic.SurfaceGroup.Items.Skip(1).Sum(s => Evaluate(optic, Row(code, s.Number, 1)));
        Assert.Equal(total, sum, 10);
        var simple = Lens(); var first = Evaluate(simple, Row(code, 1, 1));
        Value(simple, Row(code, 1, 2), first * 5 / 6);
        simple.Wavelengths[0].IsPrimary = false; simple.Wavelengths[1].IsPrimary = true;
        Value(simple, Row(code, 1, 0), first * 5 / 6);
    }

    [Theory]
    [MemberData(nameof(WaveCodes))]
    public void ChangingCurrentRadiusAndApertureRecalculatesCoefficients(string code)
    {
        var optic = Lens(); var before = Evaluate(optic, Row(code, 0, 1));
        optic.Aperture.Value *= 2;
        var factor = code == "SPHA" ? 16 : code == "COMA" ? 8 : 4;
        Value(optic, Row(code, 0, 1), before * factor);
        optic.SurfaceGroup.Items[1].Radius *= 2;
        var radiusFactor = code == "SPHA" ? 8 : code == "COMA" ? 4 : 2;
        Value(optic, Row(code, 0, 1), before * factor / radiusFactor);
    }

    [Theory]
    [InlineData("SPHA", 1)]
    [InlineData("COMA", 2)]
    [InlineData("ASTI", 3)]
    [InlineData("FCUR", 4)]
    public void Captured123456WaveTableMatchesEverySurfaceAndTotalAt440Nm(string code, int column)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixture, "zemax-123456.ZMX")), ".zmx");
        var lines = File.ReadAllText(Path.Combine(fixture, "zemax-123456-seidel-coefficients.txt")).Replace("\r", "").Split('\n');
        var start = Array.FindIndex(lines, l => l.Trim().TrimEnd(':', '：') == "赛德尔像差系数（波长）");
        Assert.True(start >= 0);
        var rows = lines.Skip(start + 1).SkipWhile(string.IsNullOrWhiteSpace).Skip(1).TakeWhile(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToArray();
        Assert.Equal(optic.SurfaceGroup.Items.Count, rows.Length);
        Assert.Equal(440, optic.Wavelengths[1].Nanometers, 6);
        for (var i = 0; i < rows.Length; i++)
        {
            var surface = i == rows.Length - 1 ? 0 : optic.SurfaceGroup.Items[i + 1].Number;
            var expected = double.Parse(rows[i][column], CultureInfo.InvariantCulture);
            var actual = Evaluate(optic, Row(code, surface, 2));
            Assert.InRange(Math.Abs(expected - actual), 0, 1e-6);
        }
    }

    [Fact]
    public void SelectedWavelengthPreservesPhysicalStopNormalization()
    {
        var optic = Optic.CreateCookeTriplet(); var stop = optic.SurfaceGroup.Items.ToList().FindIndex(s => s.IsStop);
        Assert.True(stop > 1);
        var primary = optic.Wavelengths.First(w => w.IsPrimary).Micrometers;
        var other = optic.Wavelengths.First(w => !w.IsPrimary).Micrometers;
        var ray = optic.Paraxial.MarginalRay(other); var reference = optic.Paraxial.MarginalRay(primary);
        var expectedScale = reference.Heights[stop][0] / ray.Heights[stop][0];
        var data = SeidelMetrics.Calculate(optic, other);
        Assert.Equal(ray.Slopes[^1][0] * expectedScale, data.MarginalSlopeImage, 12);
        Assert.True(Math.Abs(expectedScale - 1) > 1e-8);
    }

    [Fact]
    public void PetzvalUsesImageMediumAndIgnoresObjectAndImageRadii()
    {
        var optic = Lens(); Value(optic, Row("PETC", 0, 1), -.005); Value(optic, Row("PETZ", 0, 1), -200);
        optic.SurfaceGroup.Items[0].MaterialAfter = new ConstantIndexMaterial("ObjectMedium", 1.2);
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Interior", 1.6);
        optic.SurfaceGroup.Items[2].MaterialAfter = new ConstantIndexMaterial("ImageMedium", 1.5);
        optic.SurfaceGroup.Items[0].Geometry = new StandardGeometry(10);
        optic.SurfaceGroup.Items[2].Geometry = new StandardGeometry(-10);
        Value(optic, Row("PETC", 999, 1), -.003125); Value(optic, Row("PETZ", 999, 1), -320);
        optic.SurfaceGroup.Items[0].Radius = -20; optic.SurfaceGroup.Items[2].Radius = 30;
        Value(optic, Row("PETC", 0, 1), -.003125);
    }

    [Fact]
    public void PetzvalZeroAndConicHaveExplicitDistinctSemantics()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(100, -1);
        Value(optic, Row("PETC", 0, 1), -.005); Invalid(optic, Row("SPHA", 0, 1));
        optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry();
        Value(optic, Row("PETC", 0, 1), 0); Invalid(optic, Row("PETZ", 0, 1));
    }

    [Fact]
    public void PetzvalReportAndOperandsShareSelectedWavelengthAndImageIndex()
    {
        var optic = Lens(); var report = new SeidelCoefficientsAnalysis(optic, 2).GenerateData();
        Assert.Equal(Evaluate(optic, Row("PETZ", 0, 2)), (double)report.Values["PetzvalRadius"], 12);
        Assert.Equal(1, Evaluate(optic, Row("PETC", 0, 2)) * Evaluate(optic, Row("PETZ", 0, 2)), 12);
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void InvalidWavelengthAndEmptyTableFail(string code)
    {
        var optic = Lens(); Invalid(optic, Row(code, 0, -1)); Invalid(optic, Row(code, 0, 99));
        optic.Wavelengths.Clear(); Invalid(optic, Row(code, 0, 0));
    }

    [Theory]
    [MemberData(nameof(WaveCodes))]
    public void InvalidSurfaceFailsInsteadOfSelectingTotal(string code)
    {
        Invalid(Lens(), Row(code, -1, 1)); Invalid(Lens(), Row(code, 999, 1));
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void UnsupportedModelsFailInsteadOfUsingSphericalApproximation(string code)
    {
        var optic = Lens(); var s = optic.SurfaceGroup.Items[1];
        s.CoordinateSystem = new(new(1, 0, 0)); Invalid(optic, Row(code, 0, 1));
        s.CoordinateSystem = new(new(0, 0, 0)); s.InteractionModel = new ThinLensInteractionModel(50); Invalid(optic, Row(code, 0, 1));
        s.InteractionModel = new RefractiveReflectiveInteractionModel(); s.IsReflective = true; Invalid(optic, Row(code, 0, 1));
        s.IsReflective = false; s.Geometry = new EvenAsphereGeometry(100, 0, [.01]); Invalid(optic, Row(code, 0, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidMaterialCannotBecomeAirOrZero(double index)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Invalid", index);
        Invalid(optic, Row("PETC", 0, 1)); Invalid(optic, Row("SPHA", 0, 1));
        var report = new SeidelCoefficientsAnalysis(optic).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, report.Outcome); Assert.Null(report.Table);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ReportDoesNotSilentlySubstitutePrimaryForInvalidWave(int wave)
    {
        var data = new SeidelCoefficientsAnalysis(Lens(), wave).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome); Assert.Null(data.Table);
    }

    [Fact]
    public void UnsupportedReportAndDiagramShowUnavailableReason()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(100, -1);
        foreach (var data in new[] { new SeidelCoefficientsAnalysis(optic).GenerateData(), new SeidelDiagramAnalysis(optic).GenerateData() })
        {
            Assert.Equal(AnalysisOutcome.Unavailable, data.Outcome); Assert.Null(data.Table);
            Assert.Contains("圆锥", data.OutcomeReason);
        }
    }

    [Fact]
    public void ZeroObjectThicknessIsFiniteAndCoincidentPupilIsRejected()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[0].Thickness = 100; optic.SurfaceGroup.Renumber();
        Assert.True(double.IsFinite(Evaluate(optic, Row("SPHA", 0, 1))));
        optic.SurfaceGroup.Items[0].Thickness = 0; optic.SurfaceGroup.Renumber(); Invalid(optic, Row("SPHA", 0, 1));
        Value(optic, Row("PETC", 0, 1), -.005); // Petzval requires no marginal ray.
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void ApplicationEditingAndHelpExposeExecutableOperands(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 2, 0, 0, 0, 0, 0, 0, 1, 0, 0, "", ZemaxInt1: 0, ZemaxInt2: 2)]);
        var row = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(row.Error);
        app.Optimization.SetMeritFunction([row]); Assert.Equal(row.Value, Assert.Single(app.Optimization.GetMeritFunction()).Value);
        Assert.False(Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code).CompatibilityOnly);
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void CancellationPropagates(string code)
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(Lens(), Row(code, 0, 1)));
        Assert.ThrowsAny<OperationCanceledException>(() => new SeidelCoefficientsAnalysis(Lens()).GenerateData());
    }

    [Fact]
    public void SphericalAberrationDrivesProductionDlsCurvatureOptimization()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].RadiusVariable = true;
        var row = Row("SPHA", 0, 1); row.Target = Evaluate(optic, row) / 8; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 100);
        Assert.True(result.FinalMerit < result.InitialMerit);
        Assert.Equal(row.Target, Evaluate(runtime.CurrentOptic, row), 6);
        Assert.Equal(200, runtime.CurrentOptic.SurfaceGroup.Items[1].Radius, 2);
    }

    private static Optic Lens()
    {
        var optic = Optic.CreateBlank(); var glass = new ConstantIndexMaterial("Glass", 1.5);
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 },
            new OpticalSurface { Thickness = 50, Geometry = new StandardGeometry(100), MaterialAfter = glass, SemiDiameter = 50, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), MaterialAfter = glass, SemiDiameter = 50 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 10;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 5 });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 500, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 600, IsPrimary = false });
        return optic;
    }
    private static MeritOperandDefinition Row(string code, int surface, int wave) => new()
    {
        Type = code,
        Surface = surface,
        Wavelength = wave,
        ZemaxIntegerParameters = [surface, wave],
        ZemaxDataParameters = [0, 0, 0, 0],
        Weight = 2
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(expected, result.Value, 9); Assert.Equal(2 * Math.Pow(expected - row.Target, 2), result.Contribution, 6);
        Assert.Equal(result.Value, Evaluate(Optic.FromSnapshot(optic.ToSnapshot()), row.Clone()), 9);
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
