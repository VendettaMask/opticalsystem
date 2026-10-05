using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Apodization;
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

public sealed class BundleMetricOperandTests
{
    public static TheoryData<string> Codes => new("CENX", "CENY", "CNPX", "CNPY", "CNAX", "CNAY", "GSCE", "GSCH", "GSRE", "GSRH");

    [Theory]
    [MemberData(nameof(Codes))]
    public async Task ImportedRowsRetainSlotsTargetsWeightsAndNativeRoundTrip(string code)
    {
        var raw = code is "CENX" or "CENY" ? "1 0 7 0.125" : code.StartsWith("GS", StringComparison.Ordinal) ? "0.25 -0.5 0.125 -0.375" : "0.25 -0.5 0 7";
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 0.55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 10
            SURF 2
              DISZ 0
            {code} 1 1 {raw} 0.625 2.5 0 0
            """, ".zmx");
        var original = Assert.Single(optic.MeritFunctionOperands);
        var path = Path.Combine(Path.GetTempPath(), $"bundle-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument([optic], 0), path);
            var row = Assert.Single((await StarOptProjectStore.LoadAsync(path)).Configurations[0].MeritFunctionOperands);
            Assert.Equal(code, row.Type); Assert.Equal(original.ZemaxIntegerParameters, row.ZemaxIntegerParameters);
            Assert.Equal(original.ZemaxDataParameters, row.ZemaxDataParameters);
            Assert.Equal(.625, row.Target); Assert.Equal(2.5, row.Weight);
            Assert.True(row.Enabled); Assert.False(row.CompatibilityOnly);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void LegacyRowsUpgradeAndInvalidReferencesStayReadOnly(string code)
    {
        var optic = Plate(); var row = Row(code); row.Enabled = false; row.CompatibilityOnly = true;
        optic.MeritFunctionOperands.Add(row);
        var restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.False(restored.Enabled); Assert.False(restored.CompatibilityOnly);
        row.Wavelength = 999; row.ZemaxIntegerParameters[1] = 999;
        restored = Assert.Single(Optic.FromSnapshot(optic.ToSnapshot()).MeritFunctionOperands);
        Assert.True(restored.CompatibilityOnly);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void ApplicationAndEditorPreserveTypedSlotsAndExposeHelp(string code)
    {
        using var app = WorkbenchApplication.Create("cooke");
        var definition = Row(code);
        app.Optimization.SetMeritFunction([new(1, true, code, 0, 1, 1, 0, 0, 0, 0, 0, 1, 0, 0, "",
            ZemaxInt1: definition.ZemaxIntegerParameters[0], ZemaxInt2: 1,
            ZemaxData1: definition.ZemaxDataParameters[0], ZemaxData2: definition.ZemaxDataParameters[1],
            ZemaxData3: definition.ZemaxDataParameters[2], ZemaxData4: definition.ZemaxDataParameters[3])]);
        var dto = Assert.Single(app.Optimization.GetMeritFunction()); Assert.Empty(dto.Error);
        var type = Assert.Single(app.Optimization.GetMeritOperandTypes(), t => t.Code == code);
        Assert.False(type.CompatibilityOnly); Assert.False(string.IsNullOrWhiteSpace(type.Calculation));
        var editor = new MeritOperandEditorRow(dto, type);
        Assert.True(editor.HasZemaxParameters); Assert.True(editor.IsParameterEditable(0));
        app.Optimization.SetMeritFunction([editor.ToDto()]);
        var saved = Assert.Single(app.Optimization.GetMeritFunction());
        Assert.Empty(saved.Error); Assert.Equal(dto.Value, saved.Value, 10);
        Assert.Equal(dto.ZemaxData3, saved.ZemaxData3); Assert.Equal(dto.ZemaxData4, saved.ZemaxData4);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void CancellationAndInvalidWavelengthNeverYieldAValue(string code)
    {
        var optic = Plate(); var row = Row(code); row.ZemaxIntegerParameters[1] = 999;
        Invalid(optic, row);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => MeritFunctionCatalog.Evaluate(optic, Row(code)));
    }

    [Theory]
    [InlineData("CENX")]
    [InlineData("CNPX")]
    [InlineData("CENY")]
    [InlineData("CNPY")]
    [InlineData("CNAX")]
    [InlineData("CNAY")]
    public void RefractionAndLocalCoordinatesMatchIndependentPlaneGeometry(string code)
    {
        var optic = Plate(); var row = Row(code);
        if (code is not ("CENX" or "CENY")) row.ZemaxDataParameters[1] = 1;
        var theta = 5 * Math.PI / 180;
        var internalAngle = Math.Asin(Math.Sin(theta) / 1.5);
        var expected = code.EndsWith('X') ? 0 : code == "CNAY" ? theta : 10 * Math.Tan(internalAngle);
        Value(optic, row, expected);
        row.ZemaxIntegerParameters[0] = 1;
        Value(optic, row, code == "CNAY" ? internalAngle : 0);
        row.ZemaxIntegerParameters[0] = 2;
        optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(2, 3, 10), RotationZDegrees: 90);
        expected = code == "CNAX" ? theta : code == "CNAY" ? 0
            : code.EndsWith('X') ? 10 * Math.Tan(internalAngle) - 3 : 2;
        Value(optic, row, expected);
    }

    [Fact]
    public void NumberedFieldsDoNotOverrideExplicitAxialCoordinates()
    {
        var optic = Plate(); optic.Fields.Add(new FieldPoint { Y = 10 });
        Value(optic, Row("CNPY"), 0);
        var row = Row("CENY"); row.ZemaxDataParameters[0] = 2;
        Value(optic, row, 10 * Math.Tan(Math.Asin(Math.Sin(10 * Math.PI / 180) / 1.5)));
        row.ZemaxDataParameters[0] = 0; Invalid(optic, row);
        row.ZemaxDataParameters[0] = 1.5; Invalid(optic, row);
        row.ZemaxDataParameters[0] = 3; Invalid(optic, row);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PolychromaticCentroidUsesSpectralWeightsNotPrimaryFlag(int primary)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].MaterialAfter = new AbbeMaterial("Model", 1.5, 60);
        optic.Wavelengths.Clear();
        foreach (var (wavelength, weight, index) in new[] { (486.1, 1.0, 0), (587.6, 2.0, 1), (656.3, 4.0, 2) })
            optic.Wavelengths.Add(new Wavelength { Nanometers = wavelength, Weight = weight, IsPrimary = index == primary });
        var row = Row("CENY"); row.ZemaxIntegerParameters[1] = 0;
        var material = optic.SurfaceGroup.Items[1].MaterialAfter;
        var expected = optic.Wavelengths.Sum(w => w.Weight * 10 * Math.Tan(Math.Asin(Math.Sin(5 * Math.PI / 180) / material.RefractiveIndex(w.Nanometers)))) / 7;
        Value(optic, row, expected);
        optic.Wavelengths[0].Weight = 0; optic.Wavelengths[1].Weight = 0;
        row.ZemaxIntegerParameters[1] = 3; var mono = Evaluate(optic, row);
        row.ZemaxIntegerParameters[1] = 0; Value(optic, row, mono);
        optic.Wavelengths[2].Weight = 0; Invalid(optic, row);
        optic.Wavelengths[2].Weight = -1; Invalid(optic, row);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AsymmetricApertureAndApodizationWeightSurvivingRays(bool apodized)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(2.5, 10, 2.5, 0);
        if (apodized) optic.Apodization = new GaussianApodization(.5);
        var row = Row("CNPX"); row.ZemaxIntegerParameters[0] = 1; row.ZemaxDataParameters[3] = 5;
        double weight = 0, moment = 0;
        for (var iy = -2; iy <= 2; iy++)
            for (var ix = 0; ix <= 2; ix++)
            {
                var x = ix / 2.0; var y = iy / 2.0;
                if (x * x + y * y > 1) continue;
                var w = apodized ? Math.Exp(-(x * x + y * y) / .5) : 1;
                weight += w; moment += w * x * 5;
            }
        Value(optic, row, moment / weight);
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(1, 1, 100, 100);
        Invalid(optic, row);
    }

    [Theory]
    [InlineData("CENY", 1)]
    [InlineData("CNPX", 2)]
    [InlineData("CNAX", 2)]
    public void UnsupportedPolarizationAndInvalidGridAreExplicit(string code, int polSlot)
    {
        var optic = Plate(); var row = Row(code); row.ZemaxDataParameters[polSlot] = 1; Invalid(optic, row);
        row.ZemaxDataParameters[polSlot] = 0;
        var sampleSlot = code == "CENY" ? 2 : 3;
        foreach (var invalid in new[] { 0.0, 2, 1.5, 514, double.NaN })
        { row.ZemaxDataParameters[sampleSlot] = invalid; Invalid(optic, row); }
    }

    [Theory]
    [InlineData("GSCE")]
    [InlineData("GSCH")]
    [InlineData("GSRE")]
    [InlineData("GSRH")]
    public void GeometricRadiusMatchesDefocusedThinLensAndIsNotRms(string code)
    {
        var optic = ThinLens(); var row = Row(code); row.ZemaxIntegerParameters[0] = 2;
        var pupilRadius = code is "GSCE" or "GSCH" ? Math.Sqrt((1 + 1 / Math.Sqrt(3)) / 2) : 1;
        Value(optic, row, pupilRadius, 8); // R_pupil=5, (1 - 40/50)=.2
        optic.Aperture.Value = 20; Value(optic, row, 2 * pupilRadius, 8);
        optic.SurfaceGroup.Items[1].Thickness = 50; optic.SurfaceGroup.Renumber(); Value(optic, row, 0, 8);
    }

    [Fact]
    public void RadiusReferencesDistinguishCentroidAndChiefAfterClipping()
    {
        var optic = ThinLens(); optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(2.5, 10, 2.5, 0);
        var center = Row("GSRE"); center.ZemaxIntegerParameters[0] = 2;
        var chief = Row("GSRH"); chief.ZemaxIntegerParameters[0] = 2;
        var meanX = 2.5 / 9;
        Value(optic, center, Math.Sqrt(1 + meanX * meanX), 8);
        Value(optic, chief, 1, 8);
        Invalid(optic, Row("GSCE")); Invalid(optic, Row("GSCH"));
    }

    [Theory]
    [InlineData("GSCE")]
    [InlineData("GSCH")]
    [InlineData("GSRE")]
    [InlineData("GSRH")]
    public void GeometricSamplingAndUnverifiedZeroWaveAreNotSilentlyDefaulted(string code)
    {
        var row = Row(code); var optic = ThinLens(); row.ZemaxIntegerParameters[1] = 0; Invalid(optic, row);
        row.ZemaxIntegerParameters[1] = 1;
        foreach (var invalid in new[] { 0, -1, 257 }) { row.ZemaxIntegerParameters[0] = invalid; Invalid(optic, row); }
        row.ZemaxIntegerParameters[0] = 2; row.ZemaxDataParameters[0] = 1.1; Invalid(optic, row);
    }

    [Fact]
    public void SharedCacheReusesCentroidPairsAndDetachesOnGeometryAndWeights()
    {
        var optic = Plate(); var cache = new RayTraceCache(); optic.ConfigureRayTraceCache(cache, 1);
        var row = Row("CENY"); var value = Evaluate(optic, row); var hits = cache.Statistics.Hits;
        Evaluate(optic, Row("CENX")); Assert.True(cache.Statistics.Hits > hits);
        optic.SurfaceGroup.Items[1].Thickness = 20; optic.SurfaceGroup.Renumber();
        Value(optic, row, 2 * value);
        optic.Apodization = new GaussianApodization(.3); Value(optic, row, 2 * value);
        row.ZemaxIntegerParameters[1] = 0; optic.Wavelengths[0].Weight = 0; Invalid(optic, row);
    }

    [Fact]
    public void AngularCentroidRetainsReflectedDirectionAndUsesVectorMean()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].IsReflective = true;
        var row = Row("CNAY"); row.ZemaxIntegerParameters[0] = 1; row.ZemaxDataParameters[1] = 1;
        Value(optic, row, Math.PI - 5 * Math.PI / 180);
        WeightedLocalRay[] samples = [new(new(0, 0, 0), new(1, 0, 0), 1), new(new(0, 0, 0), new(0, 0, 1), 3)];
        Assert.Equal(Math.Atan2(1, 3), RayBundleMetrics.AngularCentroid(samples, true), 12);
        Assert.Throws<InvalidOperationException>(() => RayBundleMetrics.AngularCentroid(
            [new(new(0, 0, 0), new(1, 0, 0), 1), new(new(0, 0, 0), new(-1, 0, 0), 1)], true));
    }

    [Fact]
    public void ChiefReferenceUsesPrimaryWavelengthEvenForAnotherMonochromaticSpot()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].MaterialAfter = new AbbeMaterial("Model", 1.5, 30);
        optic.Wavelengths.Clear();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 486.1, IsPrimary = true });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 656.3 });
        var material = optic.SurfaceGroup.Items[1].MaterialAfter;
        double Height(double nm) => 10 * Math.Tan(Math.Asin(Math.Sin(5 * Math.PI / 180) / material.RefractiveIndex(nm)));
        var row = Row("GSRH"); row.ZemaxIntegerParameters[1] = 2; row.ZemaxDataParameters[1] = 1;
        Value(optic, row, 5 + Math.Abs(Height(656.3) - Height(486.1)));
        row.Type = "GSRE"; Value(optic, row, 5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void CoordinateBreakImportRemapsCentroidSurfaceButPreservesImageAlias(int sourceSurface)
    {
        var optic = OpticalFormatCatalog.Import($"""
            MODE SEQ
            ENPD 10
            WAVM 1 0.55 1
            SURF 0
              DISZ INFINITY
            SURF 1
              STOP
              DISZ 10
            SURF 2
              TYPE COORDBRK
              PARM 1 2
              DISZ 0
            SURF 3
              DISZ 0
            CNPX {sourceSurface} 1 0 0 0 5 0 1 0 0
            """, ".zmx");
        var row = Assert.Single(optic.MeritFunctionOperands);
        Assert.Equal(sourceSurface == 0 ? 0 : 2, row.ZemaxIntegerParameters[0]);
        Value(optic, row, -2);
        Value(Optic.FromSnapshot(optic.ToSnapshot()), row, -2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void FiniteAndCoincidentObjectsRemainUsable(double thickness)
    {
        var optic = Plate(); optic.SurfaceGroup.Items[0].Thickness = thickness; optic.SurfaceGroup.Renumber();
        optic.Fields[0].Y = 0;
        Value(optic, Row("CNPX"), 0); Value(optic, Row("CNPY"), 0);
    }

    [Theory]
    [InlineData("CNPX", 1.1)]
    [InlineData("CNAY", double.NaN)]
    [InlineData("GSRE", -1.1)]
    public void InvalidNormalizedFieldsAreRejected(string code, double value)
    {
        var row = Row(code); row.ZemaxDataParameters[0] = value; Invalid(Plate(), row);
    }

    [Fact]
    public void NonFiniteTraceWeightsAreRejectedAndSingleWaveIgnoresSpectralWeight()
    {
        var optic = Plate();
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Wavelengths[0].Weight = double.NaN);
        optic.Wavelengths[0].Weight = 0;
        var row = Row("CNPX"); Value(optic, row, 0);
        row.ZemaxIntegerParameters[1] = 0; Invalid(optic, row);
        optic.Wavelengths[0].Weight = 1;
        optic.SurfaceGroup.Items[2].CoordinateSystem = new(new(double.NaN, 0, 10));
        Invalid(optic, row);
    }

    [Fact]
    public void FormalOptimizationCanFocusUsingGeometricRadius()
    {
        var optic = ThinLens(); optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = Row("GSRE"); optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 70);
        Assert.True(result.FinalMerit < result.InitialMerit); Assert.Equal(50, runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness, 4);
        Assert.InRange(Evaluate(runtime.CurrentOptic, row), 0, 1e-5);
    }

    private static Optic Plate()
    {
        var optic = Optic.CreateBlank();
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity, Geometry = new PlaneGeometry(), SemiDiameter = 50 },
            new OpticalSurface { Thickness = 10, Geometry = new PlaneGeometry(), MaterialAfter = new ConstantIndexMaterial("Glass", 1.5), SemiDiameter = 50, IsStop = true },
            new OpticalSurface { Thickness = 0, Geometry = new PlaneGeometry(), MaterialAfter = new AirMaterial(), SemiDiameter = 50 }
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
        Surface = code.StartsWith("GS", StringComparison.Ordinal) ? 3 : 0,
        Wavelength = 1,
        Field = 1,
        Weight = 2,
        ZemaxIntegerParameters = [code.StartsWith("GS", StringComparison.Ordinal) ? 3 : 0, 1],
        ZemaxDataParameters = code is "CENX" or "CENY" ? [1, 0, 5, 0] : code.StartsWith("GS", StringComparison.Ordinal) ? [0, 0, 0, 0] : [0, 0, 0, 5]
    };
    private static double Evaluate(Optic optic, MeritOperandDefinition row)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error); return result.Value;
    }
    private static void Value(Optic optic, MeritOperandDefinition row, double expected, int precision = 9)
    {
        var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.Equal(expected, result.Value, precision);
        Assert.Equal(row.Weight * Math.Pow(result.Value - row.Target, 2), result.Contribution, 8);
    }
    private static void Invalid(Optic optic, MeritOperandDefinition row) => Assert.False(string.IsNullOrEmpty(MeritFunctionCatalog.Evaluate(optic, row).Error));
}
