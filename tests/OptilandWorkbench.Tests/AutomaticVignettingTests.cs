using System.Text.Json;
using OptilandWorkbench.App.ViewModels;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Scattering;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class AutomaticVignettingTests
{
    public static IEnumerable<object[]> Apertures =>
        from precision in new[] { 0, 1, 2 }
        from aiming in new[] { false, true }
        from shape in new[] { "ellipse", "rectangle", "circle" }
        select new object[] { precision, aiming, shape };

    [Theory]
    [MemberData(nameof(Apertures))]
    public void FourMarginalRaysFitPhysicalApertures(int precision, bool aiming, string shape)
    {
        var aperture = shape switch
        {
            "ellipse" => (IPhysicalAperture)new EllipticalAperture(4, 3),
            "rectangle" => new RectangularAperture(4, 3),
            _ => new CircularAperture(3)
        };
        var optic = Plane(aperture); optic.RayAimingEnabled = aiming;
        var before = Snapshot(optic);
        var result = Assert.Single(VignettingSolver.Calculate(optic, precision));
        Near(shape == "circle" ? .4 : .2, result.CompressionX, precision);
        Near(.4, result.CompressionY, precision);
        Near(0, result.DecenterX, precision); Near(0, result.DecenterY, precision);
        Assert.Equal(before, Snapshot(optic));
        VignettingSolver.Apply(optic, precision);
        AssertMarginals(optic);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(-90)]
    [InlineData(360000000090)]
    public void ShiftAndExistingTangentialRotationArePreserved(double angle)
    {
        var optic = Plane(new RectangularAperture(2, 1.5, .75, -.5));
        var field = optic.Fields[0]; field.VignetteAngleDegrees = angle;
        field.VignetteFactorX = .9; field.VignetteFactorY = .8;
        field.VignetteDecenterX = -.2; field.VignetteDecenterY = .3;
        var before = Snapshot(optic);
        var result = Assert.Single(VignettingSolver.Calculate(optic));
        Assert.Equal(angle, result.AngleDegrees);
        var center = result.Transform(0, 0);
        Near(.15, center.X); Near(-.1, center.Y);
        Near(angle == 0 ? .6 : .7, result.CompressionX);
        Near(angle == 0 ? .7 : .6, result.CompressionY);
        Assert.Equal(before, Snapshot(optic));
        VignettingSolver.Apply(optic); AssertMarginals(optic);
        Assert.Equal(result, Assert.Single(VignettingSolver.Calculate(optic)));
    }

    [Fact]
    public void OffAxisFieldHasItsOwnPupilShift()
    {
        var optic = Plane(new RectangularAperture(2, 1.5));
        optic.Fields.Add(new FieldPoint { Y = 5 });
        var result = VignettingSolver.Calculate(optic);
        Assert.Equal(2, result.Count);
        Near(0, result[0].DecenterY);
        Near(-10 * Math.Tan(5 * Math.PI / 180) / 5, result[1].DecenterY);
        Near(.6, result[1].CompressionX); Near(.7, result[1].CompressionY);
        VignettingSolver.Apply(optic); AssertMarginals(optic);
    }

    [Fact]
    public void SearchFindsAPupilEvenWhenItsChiefRayIsBlocked()
    {
        var optic = Plane(new RectangularAperture(.6, .5, 2, 1));
        VignettingSolver.Apply(optic);
        var factors = PupilVignetting.FromField(optic.Fields[0]);
        Near(.4, factors.DecenterX); Near(.2, factors.DecenterY);
        Near(.88, factors.CompressionX); Near(.9, factors.CompressionY);
        AssertMarginals(optic);
    }

    [Fact]
    public void FourRaysDoNotClaimAnUnobscuredPupilInterior()
    {
        var optic = Plane(new AnnularAperture(5, 1));
        Assert.Equal(PupilVignetting.Identity, Assert.Single(VignettingSolver.Calculate(optic)));
        var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, .55);
        Assert.False(optic.SequentialRayTracer.DiagnoseApertures(ray.Rays[0]).InsideAllApertures);
    }

    [Fact]
    public void FailedLaterFieldDoesNotPublishPartialFactors()
    {
        var optic = Plane(new RectangularAperture(2, 1.5));
        optic.Fields.Add(new FieldPoint { Y = 80, VignetteFactorX = .1 });
        var before = Snapshot(optic);
        var error = Assert.Throws<InvalidOperationException>(() => VignettingSolver.Apply(optic, 2));
        Assert.Contains("视场 2", error.Message); Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void InvalidPrecisionCannotMutateSource(int precision)
    {
        var optic = Plane(); var before = Snapshot(optic);
        Assert.Throws<ArgumentOutOfRangeException>(() => VignettingSolver.Apply(optic, precision));
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void CancellationIsNotConvertedIntoAClippedRay()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var scope = ComputationCancellation.Push(cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => VignettingSolver.Calculate(Plane()));
    }

    [Fact]
    public void ScatteringIsExplicitlyRejected()
    {
        var optic = Plane(); optic.SurfaceGroup.Items[1].ScatteringModel = new MainRayScatterLossApproximation(.1);
        Assert.Throws<NotSupportedException>(() => VignettingSolver.Calculate(optic));
    }

    [Fact]
    public void PrimaryWavelengthChangesTheComputedPupilWithoutChangingTheSource()
    {
        var optic = Lens();
        var glass = new CatalogGlassMaterial("RUNTIME:SVIG", "RUNTIME", "tabulated n", 400, 800,
            refractiveIndexWavelengthsNanometers: [400, 800], refractiveIndices: [1.45, 1.85]);
        optic.SurfaceGroup.Items[1].MaterialAfter = glass;
        optic.SurfaceGroup.Items[2].MaterialBefore = glass;
        optic.Wavelengths.Add(new Wavelength { Nanometers = 700, IsPrimary = false });
        var before = Snapshot(optic);
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [Row("SVIG"), Ray(1), Row("PRIM", 2), Row("SVIG"), Ray(1)]);
        Success(rows); Assert.True(Math.Abs(rows[1].Value - rows[4].Value) > 1e-3);
        Assert.Equal(before, Snapshot(optic));
        optic.Wavelengths[0].IsPrimary = false; optic.Wavelengths[1].IsPrimary = true;
        VignettingSolver.Apply(optic);
        Near(MeritFunctionCatalog.Evaluate(optic, Ray(1)).Value, rows[4].Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrAmbiguousPrimaryWavelengthIsAnExplicitFailure(bool duplicate)
    {
        var optic = Plane();
        if (duplicate) optic.Wavelengths.Add(new Wavelength { Nanometers = 700, IsPrimary = true });
        else optic.Wavelengths[0].IsPrimary = false;
        Assert.Contains("唯一的主波长", Assert.Throws<InvalidOperationException>(() => VignettingSolver.Calculate(optic)).Message);
    }

    [Fact]
    public void ProductionOptimizationRecomputesVignettingForEachCandidate()
    {
        var optic = Lens(); var front = optic.SurfaceGroup.Items[1];
        front.Geometry = new StandardGeometry(20);
        var ray = Ray(1); var expected = MeritFunctionCatalog.EvaluateAll(optic, [Row("SVIG"), ray]); Success(expected);
        ray.Target = expected[1].Value;
        front.Geometry = new StandardGeometry(35); front.RadiusVariable = true;
        optic.MeritFunctionOperands.Add(Row("SVIG")); optic.MeritFunctionOperands.Add(ray);
        var runtime = new WorkbenchRuntime(optic);
        var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.InitialMerit > 1e-6); Assert.True(result.FinalMerit < result.InitialMerit * 1e-4);
        var recalculated = MeritFunctionCatalog.EvaluateAll(runtime.CurrentOptic, runtime.CurrentOptic.MeritFunctionOperands); Success(recalculated);
        Assert.InRange(Math.Abs(result.FinalMerit - recalculated.Sum(row => row.Contribution)), 0, 1e-9);
        Assert.All(runtime.CurrentOptic.Fields, field => Assert.Equal(PupilVignetting.Identity, PupilVignetting.FromField(field)));
    }

    [Fact]
    public void ImageApertureParticipatesAndIntermediateImageLimitsTheSearch()
    {
        var optic = Plane(); optic.SurfaceGroup.Items[3].PhysicalAperture = new RectangularAperture(2, 1);
        var full = Assert.Single(VignettingSolver.Calculate(optic));
        Near(.6, full.CompressionX); Near(.8, full.CompressionY);
        var rows = MeritFunctionCatalog.EvaluateAll(optic,
            [Row("IMSF", 2), Row("SVIG"), Ray(1), Row("IMSF"), Row("SVIG"), Ray(1)]);
        Success(rows); Near(2, rows[2].Value); Near(1, rows[5].Value);
    }

    [Fact]
    public void StateOnlyAffectsFollowingRowsAndIgnoresTargetAndWeight()
    {
        var optic = Plane(); var before = Snapshot(optic);
        var set = Row("SVIG"); set.Target = 99; set.Weight = 42;
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [Ray(), set, Ray(), Row("CVIG"), Ray()]);
        Success(rows); Near(2.5, rows[0].Value); Near(2, rows[2].Value); Near(2.5, rows[4].Value);
        Assert.Equal(0, rows[1].Value); Assert.Equal(0, rows[1].Contribution);
        Assert.Equal(before, Snapshot(optic));
        Assert.NotEmpty(MeritFunctionCatalog.Evaluate(optic, set).Error);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("goto")]
    [InlineData("end")]
    public void ControlFlowDoesNotExecuteSkippedVignetting(string mode)
    {
        var set = Row("SVIG"); set.Enabled = mode != "disabled";
        var definitions = mode == "disabled" ? new[] { set, Ray() }
            : new[] { Row(mode == "goto" ? "GOTO" : "ENDX", 3), set, Ray() };
        var result = MeritFunctionCatalog.EvaluateAll(Plane(), definitions); Success(result);
        Near(mode == "end" ? 0 : 2.5, result[^1].Value);
    }

    [Fact]
    public void InvalidStateKeepsPreviousSuccessfulStateAndFailsOptimization()
    {
        var optic = Plane(); var definitions = new[] { Row("SVIG"), Row("SVIG", 4), Ray() };
        var rows = MeritFunctionCatalog.EvaluateAll(optic, definitions);
        Assert.NotEmpty(rows[1].Error); Assert.True(double.IsPositiveInfinity(rows[1].Contribution));
        Near(2, rows[2].Value);
        Assert.Throws<OptimizationEvaluationException>(() => MeritFunctionCatalog.EvaluateOptimizationValues(optic, definitions));
    }

    [Fact]
    public void ConfigurationSwitchAfterVignettingIsExplicitlyRejected()
    {
        var optic = Plane();
        var rows = MeritFunctionCatalog.EvaluateAll(optic, [Row("CONF", 1), Row("SVIG"), Ray(), Row("CONF", 1), Ray()]);
        Assert.Empty(rows[0].Error ?? ""); Assert.Empty(rows[1].Error ?? "");
        Assert.Contains("SVIG", rows[3].Error); Near(2, rows[2].Value); Near(2, rows[4].Value);
    }

    [Fact]
    public void SourceTraceCacheAndSubsequentBatchesRespectApertureChanges()
    {
        var optic = Plane(); var cache = new RayTraceCache(32, 512); optic.ConfigureRayTraceCache(cache, 77);
        var initial = MeritFunctionCatalog.Evaluate(optic, Ray());
        MeritFunctionCatalog.Evaluate(optic, Ray()); var hits = cache.Statistics.Hits;
        Success(MeritFunctionCatalog.EvaluateAll(optic, [Row("SVIG"), Ray()]));
        Assert.Equal(initial.Value, MeritFunctionCatalog.Evaluate(optic, Ray()).Value);
        Assert.True(cache.Statistics.Hits > hits);
        optic.SurfaceGroup.Items[2].PhysicalAperture = new CircularAperture(2);
        var next = MeritFunctionCatalog.EvaluateAll(optic, [Row("SVIG"), Ray(1)]); Success(next);
        Near(1, next[1].Value);
    }

    [Fact]
    public async Task EditablePrecisionAndTemporaryStateRoundTripThroughApplicationAndProject()
    {
        var optic = Plane(); optic.MeritFunctionOperands.Add(Row("SVIG")); optic.MeritFunctionOperands.Add(Ray());
        var path = Path.Combine(Path.GetTempPath(), $"auto-vignetting-{Guid.NewGuid():N}.staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new([optic], 0), path);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(path);
            var rows = app.Optimization.GetMeritFunction();
            var type = app.Optimization.GetMeritOperandTypes().Single(t => t.Code == "SVIG");
            Assert.False(type.CompatibilityOnly); Assert.Equal(6, type.Parameters!.Count);
            var editor = new MeritOperandEditorRow(rows[0], type);
            Assert.True(editor.IsParameterEditable(0));
            for (var slot = 1; slot < 6; slot++) Assert.False(editor.IsParameterEditable(slot));
            editor.Parameter1 = 2; app.Optimization.SetMeritFunction([editor.ToDto(), rows[1]]);
            await app.Documents.SaveAsync(path);
            var restored = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            Assert.Equal(2, restored.MeritFunctionOperands[0].ZemaxIntegerParameters[0]);
            Assert.False(restored.MeritFunctionOperands[0].CompatibilityOnly);
            Assert.All(restored.Fields, field => Assert.Equal(PupilVignetting.Identity, PupilVignetting.FromField(field)));
            Success(MeritFunctionCatalog.EvaluateAll(restored, restored.MeritFunctionOperands));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NativeUnverifiedLayoutIsPreservedAndNeverAutomaticallyEnabled()
    {
        var text = OpticalFormatCatalog.Export(Plane(new CircularAperture(50)), ".zmx") + "\nSVIG 2 19 .1 .2 .3 .4 5 7 91 92\n";
        var optic = OpticalFormatCatalog.Import(text, ".zmx");
        foreach (var copy in new[] { optic, Optic.FromSnapshot(optic.ToSnapshot()) })
        {
            var row = Assert.Single(copy.MeritFunctionOperands);
            Assert.True(row.CompatibilityOnly); Assert.False(row.Enabled);
            Assert.Equal(new[] { 2, 19 }, row.ZemaxIntegerParameters);
            Assert.Equal(new[] { .1, .2, .3, .4 }, row.ZemaxDataParameters);
            Assert.Contains("91 92", row.Comment);
        }
    }

    [Theory]
    [InlineData(".zmx")]
    [InlineData(".seq")]
    [InlineData(".len")]
    [InlineData(".txt")]
    public void UnverifiedTextExportCannotLoseLocalAutomaticVignetting(string extension)
    {
        var optic = Plane(); optic.MeritFunctionOperands.Add(Row("SVIG"));
        Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(optic, extension));
    }

    private static Optic Plane(IPhysicalAperture? aperture = null)
    {
        var optic = new Optic("Automatic vignetting"); optic.Aperture.Value = 10;
        optic.Solves.KeepImageAtBackFocus = false;
        optic.Fields.Add(new FieldPoint());
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() { Thickness = double.PositiveInfinity, SemiDiameter = 50 },
            new() { Thickness = 10, SemiDiameter = 5, IsStop = true },
            new() { Thickness = 10, SemiDiameter = 50 },
            new() { SemiDiameter = 50 }
        ]);
        optic.SurfaceGroup.Items[2].PhysicalAperture = aperture ?? new RectangularAperture(4, 3);
        return optic;
    }

    private static Optic Lens()
    {
        var optic = Plane(); var glass = new ConstantIndexMaterial("test glass", 1.5);
        optic.SurfaceGroup.Items[1].Geometry = new StandardGeometry(25);
        optic.SurfaceGroup.Items[1].MaterialAfter = glass;
        optic.SurfaceGroup.Items[2].MaterialBefore = glass;
        foreach (var surface in optic.SurfaceGroup.Items) surface.SemiDiameterFixed = true;
        return optic;
    }

    private static MeritOperandDefinition Row(string code, int parameter = 0) => new()
    { Type = code, ZemaxIntegerParameters = [parameter, 0], ZemaxDataParameters = [0, 0, 0, 0] };
    private static MeritOperandDefinition Ray(int surface = 3) => new()
    { Type = "REAX", Surface = surface, Wavelength = 1, Px = .5, ZemaxIntegerParameters = [surface, 1], ZemaxDataParameters = [0, 0, .5, 0] };
    private static string Snapshot(Optic optic) => JsonSerializer.Serialize(optic.ToSnapshot(), new JsonSerializerOptions
    { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
    private static void Near(double expected, double actual, int precision = 0) =>
        Assert.InRange(Math.Abs(expected - actual), 0, precision switch { 0 => 2e-6, 1 => 2e-4, _ => .02 });
    private static void Success(IEnumerable<MeritOperandEvaluation> rows) =>
        Assert.All(rows, row => Assert.True(string.IsNullOrEmpty(row.Error), row.Error));
    private static void AssertMarginals(Optic optic)
    {
        foreach (var field in optic.Fields)
        {
            var normalized = FieldCoordinates.Normalize(optic.Fields, field.X, field.Y);
            foreach (var (x, y) in new[] { (-1d, 0d), (1d, 0d), (0d, -1d), (0d, 1d) })
            {
                var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(normalized.X, normalized.Y, x, y, .55, optic.RayAimingEnabled);
                Assert.True(optic.SequentialRayTracer.DiagnoseApertures(ray.Rays[0], stopAtIncidentImage: true).InsideAllApertures);
            }
        }
    }
}
