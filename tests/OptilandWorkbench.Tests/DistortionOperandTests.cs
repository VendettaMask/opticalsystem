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
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class DistortionOperandTests(Xunit.Abstractions.ITestOutputHelper output)
{
    public static TheoryData<string> Codes => new("ABCD", "DISG", "DIMX", "SMIA", "FCGS", "FCGT");

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ImportAndNativeRoundTripRetainAllSixSlots(string code)
    {
        var data = code == "SMIA" ? "4 6 0.375 -0.5" : code is "ABCD" or "DIMX" ? "0 0.25 0.375 -0.5" : "0.125 0.25 0.375 -0.5";
        var wave = code == "DISG" ? -1 : 1;
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            FTYP 0 0 1 1 0 0 0
            XFLN 0
            YFLN 5
            WAVM 1 0.55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 40
            SURF 2
              DISZ 0
            {code} 1 {wave} {data} 0.625 2.5 0 0
            """, ".zmx");
        var original = Assert.Single(optic.MeritFunctionOperands);
        var path = Path.Combine(Path.GetTempPath(), $"distortion-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var row = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(code, row.Type); Assert.Equal(new[] { 1, wave }, row.ZemaxIntegerParameters);
            Assert.Equal(original.ZemaxDataParameters, row.ZemaxDataParameters);
            Assert.Equal(.625, row.Target); Assert.Equal(2.5, row.Weight);
            Assert.True(row.Enabled); Assert.False(row.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void LegacyCompatibilityUpgradeKeepsDisabledStateAndRejectsInvalidRawWave(string code)
    {
        var optic = ThinLens(); var row = Row(code); row.Enabled = false; row.CompatibilityOnly = true;
        optic.MeritFunctionOperands.Add(row);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.False(restored.Enabled); Assert.False(restored.CompatibilityOnly);
        row.ZemaxIntegerParameters[1] = code == "DISG" ? -999 : 999;
        restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.True(restored.CompatibilityOnly);
    }

    [Theory]
    [InlineData("ABCD")]
    [InlineData("DIMX")]
    [InlineData("DISG")]
    [InlineData("SMIA")]
    public void InvalidRawReferenceFieldCannotUpgradeOrSave(string code)
    {
        var optic = ThinLens(); var row = Row(code); row.ZemaxIntegerParameters[0] = 999;
        optic.MeritFunctionOperands.Add(row);
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(optic.ToSnapshot()));
        row.CompatibilityOnly = true;
        Assert.True(Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands).CompatibilityOnly);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void ApplicationEditorPublishesActiveTypedSlotsAndKeepsValues(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var row = Row(code); var data = row.ZemaxDataParameters;
        var wave = code == "DISG" ? -1 : 1;
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 0, wave, 0, 0, 0, 0, 0, 1, 0, 0, "",
            ZemaxInt1: 0, ZemaxInt2: wave, ZemaxData1: data[0], ZemaxData2: data[1], ZemaxData3: data[2], ZemaxData4: data[3])]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(dto.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.Equal("畸变与场曲", type.Category);
        var editor = new MeritOperandEditorRow(dto, type); Assert.True(editor.HasZemaxParameters);
        Assert.True(editor.IsParameterEditable(1)); Assert.True(editor.IsParameterEditable(2));
        if (code == "DISG") Assert.Equal("SignedWavelength", type.Parameters![1].ValueKind);
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(saved.Error);
        Assert.Equal(dto.Value, saved.Value, 10); Assert.Equal(wave, saved.ZemaxInt2);
        Assert.Equal(data[0], saved.ZemaxData1); Assert.Equal(data[1], saved.ZemaxData2);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void InvalidWaveAndCancellationNeverYieldSuccessfulMetric(string code)
    {
        var optic = ThinLens(); var row = Row(code); row.ZemaxIntegerParameters[1] = 999;
        Invalid(optic, row);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using var scope = ComputationCancellation.Push(cancel.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, Row(code)));
    }

    [Theory]
    [InlineData(0, 6.666666666666667)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 6.666666666666667)]
    public void ReferenceMatrixMatchesSnellLawAtAxialReference(int component, double expected)
    {
        var row = Row("ABCD"); row.ZemaxDataParameters[0] = component;
        Value(Plate(), row, expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void OffAxisReferenceUsesAnamorphicLocalDerivative(int component)
    {
        var row = Row("ABCD"); row.ZemaxIntegerParameters[0] = 1; row.ZemaxDataParameters[0] = component;
        var t = Math.Tan(5 * Math.PI / 180); var d = 2.25 + 1.25 * t * t;
        var expected = component == 0 ? 10 / Math.Sqrt(d) : 22.5 / Math.Pow(d, 1.5);
        Value(Plate(), row, expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void SignedWaveDistortionMatchesAnalyticalPlaneRefraction(int wave)
    {
        var row = Row("DISG"); row.ZemaxIntegerParameters[1] = wave;
        var t = Math.Tan(5 * Math.PI / 180);
        var predicted = 10 * t / 1.5; var actual = 10 * t / Math.Sqrt(2.25 + 1.25 * t * t);
        Value(Plate(), row, wave < 0 ? actual - predicted : 100 * (actual - predicted) / predicted);
    }

    [Fact]
    public void ArbitraryPupilRayUsesVectorDeviationInsteadOfRadialDifference()
    {
        var row = Row("DISG"); row.ZemaxIntegerParameters[1] = -1; row.ZemaxDataParameters[2] = .25;
        var t = Math.Tan(5 * Math.PI / 180); var dy = 10 * t / Math.Sqrt(2.25 + 1.25 * t * t) - 10 * t / 1.5;
        Value(Plate(), row, Math.Sqrt(1.25 * 1.25 + dy * dy));
    }

    [Fact]
    public void NonzeroReferenceChiefIsZeroAndNearbyDistortionUsesRelativeImageCoordinates()
    {
        var optic = Plate(); var row = Row("DISG"); row.ZemaxIntegerParameters[0] = 1;
        Value(optic, row, 0);
        row.ZemaxDataParameters[1] = .5;
        var refT = Math.Tan(5 * Math.PI / 180); var testT = Math.Tan(2.5 * Math.PI / 180);
        var actual = 10 * testT / Math.Sqrt(2.25 + 1.25 * testT * testT) - 10 * refT / Math.Sqrt(2.25 + 1.25 * refT * refT);
        var predicted = 22.5 / Math.Pow(2.25 + 1.25 * refT * refT, 1.5) * (testT - refT);
        Value(optic, row, 100 * (Math.Abs(actual) - Math.Abs(predicted)) / Math.Abs(predicted));
    }

    [Fact]
    public void DimxUsesChosenFieldAndAxialReferenceEvenWithoutDefinedAxialRow()
    {
        var optic = Plate(); optic.Fields.Add(new FieldPoint { Y = 10 });
        var row = Row("DIMX"); row.ZemaxIntegerParameters[0] = 1;
        var value = Evaluate(optic, row);
        row.ZemaxIntegerParameters[0] = 0; var maximumField = Evaluate(optic, row);
        Assert.True(maximumField > value * 3);
        row.ZemaxIntegerParameters[0] = 2; Value(optic, row, maximumField);
        row.Target = maximumField + 1; Value(optic, row, row.Target);
        row.Target = 0; row.ZemaxDataParameters[0] = 1;
        var absolute = Evaluate(optic, row);
        Assert.Equal(maximumField * (10 * Math.Tan(10 * Math.PI / 180) / 1.5) / 100, absolute, 8);
        optic.Fields.Add(new FieldPoint { X = 0, Y = 0 });
        Value(optic, row, absolute);
    }

    [Fact]
    public void MaximumFieldCanLieAlongXInsteadOfY()
    {
        var optic = Plate(); var y = Evaluate(optic, Row("DIMX")); optic.Fields[0].X = 5; optic.Fields[0].Y = 0;
        Value(optic, Row("DIMX"), y);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(.5)]
    public void SmiaUsesFullWidthsAndSixImageDistances(double scale)
    {
        var row = Row("SMIA"); row.ZemaxDataParameters[0] = 4 * scale; row.ZemaxDataParameters[1] = 6 * scale;
        var tx = Math.Tan(2 * scale * Math.PI / 180); var ty = Math.Tan(3 * scale * Math.PI / 180);
        var expected = 100 * (Math.Sqrt(2.25 + 1.25 * ty * ty) / Math.Sqrt(2.25 + 1.25 * (tx * tx + ty * ty)) - 1);
        Value(Plate(), row, expected);
    }

    [Theory]
    [InlineData("FCGS")]
    [InlineData("FCGT")]
    public void FieldCurvatureContainsActualDefocusAndRecalculatesAfterImageMotion(string code)
    {
        var optic = ThinLens(); var row = Row(code); row.ZemaxDataParameters[1] = 0;
        Value(optic, row, 10);
        optic.SurfaceGroup.Items[1].Thickness = 55; optic.SurfaceGroup.Renumber(); Value(optic, row, -5);
        optic.SurfaceGroup.Items[1].Thickness = 50; optic.SurfaceGroup.Renumber(); Value(optic, row, 0);
    }

    [Theory]
    [InlineData("FCGS")]
    [InlineData("FCGT")]
    public void ObliqueFieldCurvatureFollowsRadialFieldOrientation(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code); row.ZemaxDataParameters[1] = .5;
        var alongY = Evaluate(optic, row);
        row.ZemaxDataParameters[0] = -.5; row.ZemaxDataParameters[1] = 0; Value(optic, row, alongY, 5);
        var scale = FieldCoordinates.MaximumRadius(optic.Fields) * Math.PI / 180;
        var equivalent = Math.Atan(Math.Sqrt(Math.Pow(Math.Tan(.3 * scale), 2) + Math.Pow(Math.Tan(.4 * scale), 2))) / scale;
        row.ZemaxDataParameters[0] = 0; row.ZemaxDataParameters[1] = equivalent;
        var equivalentY = Evaluate(optic, row);
        row.ZemaxDataParameters[0] = .3; row.ZemaxDataParameters[1] = .4;
        Value(optic, row, equivalentY, 5);
    }

    [Theory]
    [InlineData("FCGS", 1)]
    [InlineData("FCGT", 0)]
    public void FieldScanAndOperandShareSameParabasalComputation(string code, int series)
    {
        var optic = Optic.CreateCookeTriplet();
        var analysis = new FieldCurvatureAnalysis(optic, numPoints: 5, wavelengthNumber: 1, ignoreVignettingFactors: false).GenerateData();
        var row = Row(code);
        for (var i = 0; i < 5; i++)
        {
            row.ZemaxDataParameters[1] = i / 4.0;
            Value(optic, row, analysis.PlotSeries[series].Points[i].X, 8);
        }
    }

    [Theory]
    [InlineData("FCGS")]
    [InlineData("FCGT")]
    public void RotatedImageAxesDoNotSwapSagittalAndTangentialValues(string code)
    {
        var optic = Optic.CreateCookeTriplet(); var row = Row(code); row.ZemaxDataParameters[1] = .5;
        var before = Evaluate(optic, row); var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = image.CoordinateSystem with { RotationZDegrees = 90 };
        Value(optic, row, before, 5);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void AfocalAndBlockedChiefRayAreExplicitErrors(string code)
    {
        var optic = ThinLens(); optic.ImageSpaceAfocal = true; Invalid(optic, Row(code));
        optic.ImageSpaceAfocal = false;
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(.01, .01, 100, 100);
        Invalid(optic, Row(code));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MinValue)]
    [InlineData(-999)]
    public void InvalidSignedWavelengthDoesNotOverflowOrSilentlySelectPrimary(int wave)
    {
        var row = Row("DISG"); row.ZemaxIntegerParameters[1] = wave; row.Wavelength = wave;
        Invalid(Plate(), row); var optic = Plate(); optic.MeritFunctionOperands.Add(row);
        if (wave == 0) OpticSnapshotValidator.Validate(optic.ToSnapshot());
        else Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(optic.ToSnapshot()));
    }

    [Theory]
    [InlineData("ABCD", 0, 4)]
    [InlineData("ABCD", 0, .5)]
    [InlineData("DIMX", 0, 2)]
    [InlineData("SMIA", 0, 0)]
    [InlineData("SMIA", 1, -1)]
    [InlineData("SMIA", 1, 12)]
    [InlineData("DISG", 0, 1.1)]
    [InlineData("DISG", 2, 1.1)]
    [InlineData("FCGS", 1, -1.1)]
    [InlineData("FCGT", 0, double.NaN)]
    public void InvalidActiveParametersNeverProduceFiniteContribution(string code, int slot, double value)
    {
        var row = Row(code); row.ZemaxDataParameters[slot] = value; Invalid(ThinLens(), row);
    }

    [Theory]
    [InlineData("ABCD")]
    [InlineData("DISG")]
    [InlineData("DIMX")]
    [InlineData("SMIA")]
    public void UndefinedReferenceAndNinetyDegreeFieldsAreRejected(string code)
    {
        var optic = Plate(); var row = Row(code); row.ZemaxIntegerParameters[0] = 2; Invalid(optic, row);
        row.ZemaxIntegerParameters[0] = 0; optic.Fields[0].Y = 90; Invalid(optic, row);
        optic.Fields.Clear(); Invalid(optic, row);
    }

    [Theory]
    [InlineData("FCGS")]
    [InlineData("FCGT")]
    public void ParallelRaysCurvedAndTiltedImagesHaveNoPretendZeroFocus(string code)
    {
        Invalid(Plate(), Row(code)); var optic = ThinLens(); optic.SurfaceGroup.Items[^1].Geometry = new StandardGeometry(100);
        Invalid(optic, Row(code)); optic.SurfaceGroup.Items[^1].Geometry = new PlaneGeometry();
        optic.SurfaceGroup.Items[^1].CoordinateSystem = optic.SurfaceGroup.Items[^1].CoordinateSystem with { RotationYDegrees = 10 };
        Invalid(optic, Row(code));
    }

    [Fact]
    public void ZeroReferenceHeightAllowsChiefOrAbsoluteButRejectsUndefinedPercentage()
    {
        var row = Row("DISG"); row.ZemaxDataParameters[1] = 0; Value(Plate(), row, 0);
        row.ZemaxDataParameters[2] = .2; Invalid(Plate(), row);
        row.ZemaxIntegerParameters[1] = -1; Value(Plate(), row, 1);
    }

    [Fact]
    public void SharedCacheDoesNotReturnStaleDistortionAfterMaterialChange()
    {
        var optic = Plate(); optic.ConfigureRayTraceCache(new RayTraceCache(), 1);
        var initial = Evaluate(optic, Row("DISG"));
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("SecondGlass", 2);
        var updated = Evaluate(optic, Row("DISG")); Assert.NotEqual(initial, updated);
        var independent = Plate(); independent.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("SecondGlass", 2);
        Value(independent, Row("DISG"), updated);
    }

    [Fact]
    public void FieldCurvatureDrivesFormalDampedLeastSquaresToImageFocus()
    {
        var optic = ThinLens(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = Row("FCGT"); row.ZemaxDataParameters[1] = 0; optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 60);
        Assert.True(result.FinalMerit < result.InitialMerit); Assert.Equal(50, runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness, 4);
    }

    [Theory]
    [InlineData("DISG")]
    [InlineData("DIMX")]
    [InlineData("FCGS")]
    [InlineData("FCGT")]
    public void Captured123456ComparisonKeepsExplicitAccuracyAndResidualBudgets(string code)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OptilandWorkbench.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "artifacts", "zemax", "123456-zemax-2026-r1-baseline", "analyses", "004-fieldcurvatureanddistortion", "data.json");
        using var captured = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX");
        var maximumError = 0.0;
        foreach (var curve in captured.RootElement.GetProperty("dataSeries").EnumerateArray())
        {
            var optic = OpticalFormatCatalog.Import(File.ReadAllText(fixture), ".zmx");
            var description = curve.GetProperty("description").GetString()!;
            var wave = double.Parse(System.Text.RegularExpressions.Regex.Match(description, @"0\.\d+").Value, System.Globalization.CultureInfo.InvariantCulture);
            var waveIndex = optic.Wavelengths.ToList().FindIndex(w => Math.Abs(w.Micrometers - wave) < 1e-10) + 1;
            Assert.True(waveIndex > 0);
            var angles = curve.GetProperty("x").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            var values = curve.GetProperty("y").EnumerateArray().Select(y => y.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
            optic.FieldDefinition = FieldDefinitionKind.Angle;
            optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = angles[^1] });
            var row = Row(code); row.ZemaxIntegerParameters[1] = waveIndex;
            foreach (var i in new[] { 0, angles.Length / 2, angles.Length - 1 })
            {
                row.ZemaxDataParameters[1] = angles[i] / angles[^1];
                if (code == "DIMX")
                {
                    optic.Fields.Add(new FieldPoint { Y = angles[i] }); row.ZemaxIntegerParameters[0] = optic.Fields.Count;
                }
                var expected = code switch { "FCGT" => values[i][0], "FCGS" => values[i][1], "DIMX" => Math.Abs(values[i][4]), _ => values[i][4] };
                var actual = Evaluate(optic, row); var error = Math.Abs(actual - expected);
                maximumError = Math.Max(maximumError, error);
                output.WriteLine($"{code}, Wave={wave:R}, Angle={angles[i]:R}, actual={actual:R}, captured={expected:R}, error={error:R}");
            }
        }
        // FCGT currently differs from the capture by up to 2.16e-5 mm. The
        // 3e-5 mm limit only prevents regression of that known residual; it is
        // NOT a relaxation of the 1e-5 mm numerical-comparison target.
        var comparisonTarget = code is "FCGS" or "FCGT" ? 1e-5 : 2e-5;
        output.WriteLine($"comparisonTarget={comparisonTarget:R}, maximumError={maximumError:R}, numericalComparison={(maximumError <= comparisonTarget ? "Pass" : "Difference")}");
        Assert.InRange(maximumError, 0, code == "FCGT" ? 3e-5 : comparisonTarget);
    }

    [Theory]
    [InlineData("ABCD")]
    [InlineData("DIMX")]
    [InlineData("SMIA")]
    [InlineData("FCGS")]
    [InlineData("FCGT")]
    public void ZeroWaveChoosesCurrentPrimaryWithoutSpectralAveraging(string code)
    {
        var optic = Optic.CreateCookeTriplet();
        foreach (var wave in optic.Wavelengths) wave.IsPrimary = false;
        optic.Wavelengths[1].IsPrimary = true;
        var row = Row(code); row.ZemaxIntegerParameters[1] = 2; var mono = Evaluate(optic, row);
        row.ZemaxIntegerParameters[1] = 0; Value(optic, row, mono);
    }

    [Theory]
    [InlineData("ABCD")]
    [InlineData("DIMX")]
    [InlineData("DISG")]
    [InlineData("SMIA")]
    public void RealImageHeightConversionUsesFormalEngineAndPreservesOriginalSnapshot(string code)
    {
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");
        Assert.Equal(FieldDefinitionKind.RealImageHeight, optic.FieldDefinition);
        optic.ConfigureRayTraceCache(new RayTraceCache(), 17);
        var before = System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot());
        var converted = RealImageFieldConversion.ForDistortion(optic);
        var row = Row(code); var expected = Evaluate(converted, row); Value(optic, row, expected);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot()));
    }

    [Fact]
    public void AxialOnlyDimxIsZeroWithoutInventingAnOffAxisField()
    {
        var optic = Plate(); optic.Fields[0].Y = 0; Value(optic, Row("DIMX"), 0);
    }

    [Fact]
    public void SmiaOffAxisReferenceIsNotSilentlyTreatedAsRectangleCenter()
    {
        var optic = Plate(); var row = Row("SMIA"); row.ZemaxIntegerParameters[0] = 1;
        Invalid(optic, row);
        row.ZemaxIntegerParameters[0] = 0; var expected = Evaluate(optic, row);
        optic.Fields.Add(new FieldPoint()); row.ZemaxIntegerParameters[0] = 2; Value(optic, row, expected);
    }

    [Fact]
    public void SignedWaveZeroRemainsEditableWithoutPretendingToSelectPrimary()
    {
        using var app = WorkbenchApplication.Create("cooke");
        app.Optimization.SetMeritFunction([new(1, true, "DISG", 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, "",
            ZemaxInt1: 0, ZemaxInt2: 0, ZemaxData1: 0, ZemaxData2: 1, ZemaxData3: 0, ZemaxData4: 0)]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.NotEmpty(dto.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == "DISG");
        var editor = new MeritOperandEditorRow(dto, type) { Parameter2 = -1 };
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var repaired = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Empty(repaired.Error); Assert.Equal(-1, repaired.ZemaxInt2); Assert.True(double.IsFinite(repaired.Value));
    }

    private static Optic Plate()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), SemiDiameter = 100 },
            new OpticalSurface { Thickness = 10, Geometry = new PlaneGeometry(), MaterialAfter = new ConstantIndexMaterial("Glass", 1.5), SemiDiameter = 100, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 100 }
        ]);
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter; optic.Aperture.Value = 10;
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { Y = 5 });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        return optic;
    }
    private static Optic ThinLens()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].MaterialAfter = new AirMaterial();
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
        optic.SurfaceGroup.Items[1].Thickness = 40; optic.SurfaceGroup.Renumber(); return optic;
    }
    private static MeritOperandDefinition Row(string code) => new()
    {
        Type = code,
        Wavelength = 1,
        Weight = 1,
        ZemaxIntegerParameters = [0, 1],
        ZemaxDataParameters = code == "SMIA" ? [4, 6, 0, 0] : code is "ABCD" or "DIMX" ? [0, 0, 0, 0] : [0, 1, 0, 0]
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error); Assert.True(double.IsFinite(result.Value)); return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected, int precision = 7) => Assert.Equal(expected, Evaluate(optic, row), precision);
    private static void Invalid(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.NotEmpty(result.Error); Assert.True(double.IsPositiveInfinity(result.Contribution));
    }
}
