using System.Globalization;
using OptilandWorkbench.App.ViewModels;
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

public sealed class ExplicitDistortionOperandTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(100, 5)]
    [InlineData(-100, 5)]
    [InlineData(100, -5)]
    [InlineData(200, 10)]
    public void ThirdOrderSingleInterfaceUsesWavesPercentAndSignedLength(double radius, double field)
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Radius = radius; optic.Fields[0].Y = field;
        var t = Math.Tan(field * Math.PI / 180); const double n = 1.5;
        var s5 = (1 - 1 / (n * n)) * 5 * t * t * t;
        Value(optic, Dist(1), s5 / .001);
        Value(optic, Dist(1, absolute: 1), s5 / .001); // Absolute does not change surface units.
        Value(optic, Dist(), 50 * (1 - 1 / (n * n)) * t * t);
        Value(optic, Dist(absolute: 1), radius * (n + 1) / (2 * n * n) * t * t * t);
        Value(optic, Dist(2), 0); // Unpowered image surface contributes zero waves.
    }

    [Fact]
    public void ThirdOrderIsIndependentOfApertureAndImageDefocusButSurfaceWavesScaleWithAperture()
    {
        var optic = Lens(); var percent = Evaluate(optic, Dist()); var length = Evaluate(optic, Dist(absolute: 1));
        var waves = Evaluate(optic, Dist(1));
        optic.Aperture.Value *= 2;
        Value(optic, Dist(), percent); Value(optic, Dist(absolute: 1), length); Value(optic, Dist(1), 2 * waves);
        optic.SurfaceGroup.Items[1].Thickness = 100; optic.SurfaceGroup.Renumber();
        Value(optic, Dist(), percent); Value(optic, Dist(absolute: 1), length);
        optic.Fields[0].Y = 0; Value(optic, Dist(), 0); Value(optic, Dist(absolute: 1), 0);
    }

    [Fact]
    public void ThirdOrderWavelengthPrimaryAndRawSurfaceOverrideAliases()
    {
        var optic = Lens(); var row = Dist(1); var expected = Evaluate(optic, row);
        optic.Wavelengths.Add(new Wavelength { Nanometers = 600 });
        row.ZemaxIntegerParameters[1] = 2; row.Surface = 999; row.Wavelength = 999;
        Value(optic, row, expected * 5 / 6);
        optic.Wavelengths[0].IsPrimary = false; optic.Wavelengths[1].IsPrimary = true;
        row.ZemaxIntegerParameters[1] = 0; Value(optic, row, expected * 5 / 6);
    }

    [Fact]
    public void Captured123456SurfaceW311AndTotalTransverseDistortionMatchAt440Nm()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(fixture, "zemax-123456.ZMX")), ".zmx");
        var lines = File.ReadAllText(Path.Combine(fixture, "zemax-123456-seidel-coefficients.txt")).Replace("\r", "").Split('\n');
        string[][] Section(string heading)
        {
            var start = Array.FindIndex(lines, line => line.Trim().TrimEnd(':', '：') == heading);
            Assert.True(start >= 0, heading);
            return lines.Skip(start + 1).SkipWhile(string.IsNullOrWhiteSpace).Skip(1)
                .TakeWhile(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToArray();
        }
        var rows = Section("赛德尔像差系数（波长）"); var max = 0.0;
        Assert.Equal(optic.SurfaceGroup.Items.Count, rows.Length);
        for (var i = 0; i < rows.Length - 1; i++)
        {
            var expected = double.Parse(rows[i][5], CultureInfo.InvariantCulture);
            max = Math.Max(max, Math.Abs(expected - Evaluate(optic, Dist(optic.SurfaceGroup.Items[i + 1].Number, wave: 2))));
        }
        Assert.InRange(max, 0, 1e-6);
        var transverse = Section("横向像差系数");
        var total = double.Parse(transverse[^1][8], CultureInfo.InvariantCulture);
        var absoluteError = Math.Abs(total - Evaluate(optic, Dist(absolute: 1, wave: 2)));
        Assert.InRange(absoluteError, 0, 1e-6);
        output.WriteLine($"DIST captured Seidel report: {rows.Length - 1} surface W311 values max error={max:R} waves; total TDIS error={absoluteError:R} mm. This is report evidence, not native DIST MFE capture.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExplicitMatrixMatchesIndependentSnellRayWithOffAxisReferenceAndCrossTerms(int component)
    {
        var optic = Plate(); optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { X = 2, Y = -3 });
        optic.Fields.Add(new FieldPoint { X = -4, Y = 6 });
        var row = Disa(component); row.ZemaxIntegerParameters[0] = 1;
        row.ZemaxDataParameters = [2, component, 6, .7, -.4, 7];
        var origin = Snell(2, -3); var image = Snell(-4, 6);
        var dx = Tan(-4) - Tan(2); var dy = Tan(6) - Tan(-3);
        var predicted = (X: 6 * dx + .7 * dy, Y: -.4 * dx + 7 * dy);
        var actual = (X: image.X - origin.X, Y: image.Y - origin.Y);
        var radial = Math.Sqrt(Math.Pow(actual.X - predicted.X, 2) + Math.Pow(actual.Y - predicted.Y, 2));
        if (actual.X * actual.X + actual.Y * actual.Y < predicted.X * predicted.X + predicted.Y * predicted.Y) radial *= -1;
        var expected = component switch
        {
            0 => 100 * radial / Math.Sqrt(predicted.X * predicted.X + predicted.Y * predicted.Y),
            1 => 100 * (actual.X - predicted.X) / predicted.X,
            _ => 100 * (actual.Y - predicted.Y) / predicted.Y
        };
        Value(optic, row, expected);
        row.Surface = 999; row.Field = 999; row.Wavelength = 999; row.Hx = 999; row.Hy = 999;
        Value(optic, row, expected); // All eight explicit slots are authoritative.
    }

    [Fact]
    public void UserMatrixCanBeRankOneAndReferenceFieldCanBeExactlyTheTarget()
    {
        var optic = Plate(); var row = Disa(2); row.ZemaxDataParameters = [1, 2, 0, 0, 0, 10 / 1.5];
        var expected = 100 * (Snell(0, 5).Y / (10 / 1.5 * Tan(5)) - 1);
        Value(optic, row, expected);
        row.ZemaxDataParameters[1] = 0; Value(optic, row, expected);
        row.ZemaxDataParameters[1] = 1; Invalid(optic, row); // Undefined X percentage is not a successful zero.
        row.ZemaxIntegerParameters[0] = 1;
        for (var i = 0; i < 3; i++) { row.ZemaxDataParameters[1] = i; Value(optic, row, 0); }
    }

    [Fact]
    public void FiniteObjectHeightsUseHeightDifferencesInsteadOfAngularTangents()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[0].Thickness = 100; optic.SurfaceGroup.Renumber();
        optic.FieldDefinition = FieldDefinitionKind.ObjectHeight; optic.Fields[0].X = 3; optic.Fields[0].Y = 4;
        var row = Disa(); row.ZemaxDataParameters[2] = row.ZemaxDataParameters[5] = -10 / 150.0;
        var actualRadius = 10 * .05 / Math.Sqrt(2.25 + 1.25 * .05 * .05);
        Value(optic, row, 100 * (actualRadius / (5 * 10 / 150.0) - 1));
    }

    [Fact]
    public void RealImageFieldConversionKeepsOriginalFieldAndTracesInAngleSpace()
    {
        var optic = Plate(); optic.FieldDefinition = FieldDefinitionKind.RealImageHeight; optic.Fields[0].Y = .5;
        var sinOut = .05 / Math.Sqrt(1 + .05 * .05); var sinIn = 1.5 * sinOut;
        var tanIn = sinIn / Math.Sqrt(1 - sinIn * sinIn);
        Value(optic, Disa(), 100 * (.5 / (10 / 1.5 * tanIn) - 1));
        Assert.Equal(FieldDefinitionKind.RealImageHeight, optic.FieldDefinition); Assert.Equal(.5, optic.Fields[0].Y);
    }

    [Fact]
    public void MatrixComponentsUseImageLocalCoordinates()
    {
        var optic = Plate(); optic.Fields[0].X = 3; optic.Fields[0].Y = 4;
        var expected = Evaluate(optic, Disa());
        var image = optic.SurfaceGroup.Items[^1]; image.CoordinateSystem = image.CoordinateSystem with { RotationZDegrees = 90 };
        var row = Disa(); row.ZemaxDataParameters = [1, 0, 0, 10 / 1.5, -10 / 1.5, 0];
        Value(optic, row, expected);
    }

    [Fact]
    public void MatrixAndFieldChangesRecalculateWithoutMutatingTheDocument()
    {
        var optic = Plate(); var row = Disa(); var original = Evaluate(optic, row);
        var field = optic.Fields[0].Clone();
        row.ZemaxDataParameters[5] = 7; Assert.NotEqual(original, Evaluate(optic, row));
        row.ZemaxDataParameters[5] = 10 / 1.5; Value(optic, row, original);
        optic.Fields[0].Y = 10; Assert.NotEqual(original, Evaluate(optic, row));
        optic.Fields[0].Y = field.Y; Value(optic, row, original);
        optic.SurfaceGroup.Items[1].Thickness = 20; optic.SurfaceGroup.Renumber(); Assert.NotEqual(original, Evaluate(optic, row));
    }

    [Fact]
    public void GridSummaryRetainsNonRadialDeviationInsteadOfSubtractingRadii()
    {
        var optic = Plate(); optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { X = 2, Y = 3 }); optic.Fields.Add(new FieldPoint { X = 5, Y = 7 });
        var data = new GridDistortionAnalysis(optic, numPoints: 7, referenceFieldNumber: 1, fieldWidth: 8).GenerateData();
        var tx = Tan(2); var ty = Tan(3); var denom = 2.25 + 1.25 * (tx * tx + ty * ty);
        var a = 10 / Math.Sqrt(denom) - 12.5 * tx * tx / Math.Pow(denom, 1.5);
        var b = -12.5 * tx * ty / Math.Pow(denom, 1.5);
        var d = 10 / Math.Sqrt(denom) - 12.5 * ty * ty / Math.Pow(denom, 1.5);
        var origin = Snell(2, 3); var half = Tan(4); var maximum = 0.0; var radialDifferenceMaximum = 0.0;
        for (var i = 0; i < 7; i++) for (var j = 0; j < 7; j++)
        {
            var x = -half + 2 * half * i / 6; var y = -half + 2 * half * j / 6;
            var xp = a * (x - tx) + b * (y - ty); var yp = b * (x - tx) + d * (y - ty);
            var actual = Snell(Math.Atan(x) * 180 / Math.PI, Math.Atan(y) * 180 / Math.PI);
            var xr = actual.X - origin.X; var yr = actual.Y - origin.Y;
            var pr = Math.Sqrt(xp * xp + yp * yp); var ar = Math.Sqrt(xr * xr + yr * yr);
            var deviation = 100 * Math.Sqrt(Math.Pow(xr - xp, 2) + Math.Pow(yr - yp, 2)) / pr;
            if (ar < pr) deviation = -deviation;
            if (Math.Abs(deviation) > Math.Abs(maximum)) maximum = deviation;
            radialDifferenceMaximum = Math.Max(radialDifferenceMaximum, Math.Abs(100 * (ar - pr) / pr));
        }
        Assert.Equal(maximum, (double)data.Values["MaximumDistortionPercent"], 6);
        Assert.True(Math.Abs(maximum) - radialDifferenceMaximum > 1e-4);
    }

    [Fact]
    public void PureImageRotationHasNonzeroVectorDeviationEvenWhenRadiiAreEqual()
    {
        Assert.Equal(Math.Sqrt(2), DistortionMetrics.SignedVectorDifference(0, 1, 1, 0), 12);
        Assert.Equal(-Math.Sqrt(1.25), DistortionMetrics.SignedVectorDifference(0, .5, 1, 0), 12);
    }

    [Theory]
    [InlineData("DIST")]
    [InlineData("DISA")]
    public async Task LocalEditorAndStarOptRoundTripKeepUnitsAllSlotsAndExecutableState(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 1, 1, 0, 0, 0, 0, .25, 2, 0, 0, "matrix",
            ZemaxInt1: 0, ZemaxInt2: 1, ZemaxData1: code == "DIST" ? 1 : 3, ZemaxData2: 0,
            ZemaxData3: 40, ZemaxData4: .1, ZemaxData5: code == "DISA" ? .2 : null, ZemaxData6: code == "DISA" ? 40 : null)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(dto.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), item => item.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Equal("畸变与场曲", type.Category); Assert.Contains("原生", type.Calculation);
        var editor = new MeritOperandEditorRow(dto, type);
        if (code == "DISA")
        {
            Assert.Equal(8, type.Parameters!.Count); Assert.True(editor.IsParameterEditable(7));
            Assert.Equal("D", editor.ParameterLabel(7)); editor.Parameter8 = 45;
        }
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var edited = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(edited.Error);
        if (code == "DISA") { Assert.Equal(.2, edited.ZemaxData5); Assert.Equal(45, edited.ZemaxData6); }
        var optic = code == "DIST" ? Lens() : Plate(); var row = code == "DIST" ? Dist() : Disa();
        optic.MeritFunctionOperands.Add(row);
        var path = Path.Combine(Path.GetTempPath(), $"explicit-distortion-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            var saved = Assert.Single(restored.MeritFunctionOperands);
            Assert.True(saved.Enabled); Assert.False(saved.CompatibilityOnly);
            Assert.Equal(row.ZemaxDataParameters, saved.ZemaxDataParameters); Value(restored, saved, Evaluate(optic, row));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("DIST")]
    [InlineData("DISA")]
    public void NativeRowsStayReadOnlyAndRetainUnverifiedExtendedRecord(string code)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 0.5 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 20
            SURF 2
              DISZ 0
            {code} 1 1 1 0 6.5 0.1 0.25 2 0.2 7.5 0 0
            """, ".zmx");
        var row = Assert.Single(optic.MeritFunctionOperands); Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
        Assert.Contains("0.2 7.5 0 0", row.Comment); Assert.Equal(4, row.ZemaxDataParameters.Length);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.True(restored.CompatibilityOnly); Assert.Equal(row.Comment, restored.Comment);
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, false, code, 1, 1, 1, 1, 0, 6.5, .1, .25, 2, 0, 0, row.Comment,
            CompatibilityOnly: true, ZemaxInt1: 1, ZemaxInt2: 1,
            ZemaxData1: 1, ZemaxData2: 0, ZemaxData3: 6.5, ZemaxData4: .1)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Null(dto.ZemaxData5); Assert.Null(dto.ZemaxData6);
        var metadata = Assert.Single(app.Optimization.GetMeritOperandTypes(), item => item.Code == code);
        var editor = new MeritOperandEditorRow(dto, metadata); Assert.False(editor.IsParameterEditable(1));
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction()); Assert.True(saved.CompatibilityOnly);
        Assert.Null(saved.ZemaxData5); Assert.Null(saved.ZemaxData6); Assert.Equal(dto.Comment, saved.Comment);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(.5)]
    [InlineData(3)]
    [InlineData(double.NaN)]
    public void InvalidDistAbsoluteNeverProducesSuccessfulZero(double flag)
    {
        var row = Dist(); row.ZemaxDataParameters[0] = flag; Invalid(Lens(), row);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 0)]
    [InlineData(0, 99)]
    [InlineData(0, .5)]
    [InlineData(1, -1)]
    [InlineData(1, 3)]
    [InlineData(1, .5)]
    [InlineData(2, double.NaN)]
    [InlineData(3, double.PositiveInfinity)]
    [InlineData(4, double.NaN)]
    [InlineData(5, double.NegativeInfinity)]
    public void InvalidExplicitSlotsFail(int slot, double value)
    {
        var row = Disa(); row.ZemaxDataParameters[slot] = value; Invalid(Plate(), row);
    }

    [Fact]
    public void IncompleteMatrixAndBadRawReferencesCannotSaveOrExecute()
    {
        var optic = Plate(); var row = Disa(); row.ZemaxDataParameters = [1, 0, 6, 0];
        optic.MeritFunctionOperands.Add(row); Invalid(optic, row);
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(optic.ToSnapshot()));
        row.ZemaxDataParameters = [1, 0, 6, 0, 0, 6];
        foreach (var invalid in new[] { -1, 999 })
        {
            row.ZemaxIntegerParameters[0] = invalid; Invalid(optic, row);
            Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(optic.ToSnapshot()));
        }
        row.ZemaxIntegerParameters[0] = 0; row.ZemaxDataParameters[0] = 999; Invalid(optic, row);
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(optic.ToSnapshot()));
    }

    [Theory]
    [InlineData("DIST")]
    [InlineData("DISA")]
    public void BadWaveUnsupportedModelsAndCancellationAreExplicit(string code)
    {
        var optic = Lens(); var row = code == "DIST" ? Dist() : Disa(); row.ZemaxIntegerParameters[1] = 99;
        Invalid(optic, row); row.ZemaxIntegerParameters[1] = -1; Invalid(optic, row); row.ZemaxIntegerParameters[1] = 1;
        optic.ImageSpaceAfocal = true; Invalid(optic, row); optic.ImageSpaceAfocal = false;
        if (code == "DIST")
        {
            row.ZemaxIntegerParameters[0] = 999; Invalid(optic, row); row.ZemaxIntegerParameters[0] = 0;
            optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(100, -1); Invalid(optic, row);
            optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry(); Invalid(optic, row);
        }
        else
        {
            optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.01, .01, 100, 100); Invalid(optic, row);
        }
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, row));
    }

    [Fact]
    public void ExplicitDistortionDrivesProductionDlsImageThickness()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = Disa(); row.ZemaxDataParameters[5] = 8; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 80);
        Assert.True(result.FinalMerit < result.InitialMerit); Assert.InRange(Math.Abs(Evaluate(runtime.CurrentOptic, row)), 0, 1e-5);
        var expectedThickness = 8 * Math.Sqrt(2.25 + 1.25 * Tan(5) * Tan(5));
        Assert.Equal(expectedThickness, runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness, 5);
    }

    private static double Tan(double degrees) => Math.Tan(degrees * Math.PI / 180);
    private static (double X, double Y) Snell(double x, double y)
    {
        var tx = Tan(x); var ty = Tan(y); var denominator = Math.Sqrt(2.25 + 1.25 * (tx * tx + ty * ty));
        return (10 * tx / denominator, 10 * ty / denominator);
    }
    private static Optic Plate()
    {
        var optic = Lens(); optic.SurfaceGroup.Items[1].Geometry = new PlaneGeometry();
        optic.SurfaceGroup.Items[1].Thickness = 10; optic.SurfaceGroup.Renumber(); return optic;
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
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 500, IsPrimary = true }); return optic;
    }
    private static MeritOperandDefinition Dist(int surface = 0, int absolute = 0, int wave = 1) => new()
    {
        Type = "DIST",
        ZemaxIntegerParameters = [surface, wave],
        ZemaxDataParameters = [absolute, 0, 0, 0],
        Weight = 2
    };
    private static MeritOperandDefinition Disa(int component = 0) => new()
    {
        Type = "DISA",
        ZemaxIntegerParameters = [0, 1],
        ZemaxDataParameters = [1, component, 10 / 1.5, 0, 0, 10 / 1.5],
        Weight = 2
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(expected, result.Value, 8); Assert.Equal(2 * Math.Pow(expected - row.Target, 2), result.Contribution, 6);
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error);
        Assert.True(double.IsNaN(result.Value)); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
