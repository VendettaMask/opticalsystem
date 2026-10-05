using System.Text.Json;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Application.Runtime;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class Gradient5MaterialTests
{
    [Theory]
    [InlineData(450)]
    [InlineData(550)]
    [InlineData(650)]
    public void ReferencePolynomialAndDispersiveGradientMatchIndependentDifferentiation(double wave)
    {
        var profile = Profile(Dispersion());
        var point = new Vector3D(.7, -.4, 1.2);
        var actual = profile.Evaluate(point, wave);
        double Reference(Vector3D p) => 1.5 - .015 * (p.X * p.X + p.Y * p.Y)
            + .0002 * Math.Pow(p.X * p.X + p.Y * p.Y, 2)
            + .01 * p.Z - .0003 * p.Z * p.Z + .00002 * Math.Pow(p.Z, 3) + .000001 * Math.Pow(p.Z, 4);
        double Index(Vector3D p)
        {
            var n = Reference(p); var w2 = wave * wave / 1e6;
            return Math.Sqrt(n * n + (.2 + .03 * n + .004 * n * n) * (w2 - .55 * .55) / (w2 - (.01 + .002 * n)));
        }
        Near(Index(point), actual.Index, 2e-15);
        const double h = 1e-5;
        foreach (var axis in new[] { new Vector3D(1, 0, 0), new(0, 1, 0), new(0, 0, 1) })
        {
            var numerical = (Index(point - axis * (2 * h)) - 8 * Index(point - axis * h)
                + 8 * Index(point + axis * h) - Index(point + axis * (2 * h))) / (12 * h);
            Near(numerical, actual.Gradient.X * axis.X + actual.Gradient.Y * axis.Y + actual.Gradient.Z * axis.Z, 5e-11);
        }
        if (wave == 550) Assert.Equal(Profile().Evaluate(point, wave), actual);
    }

    [Fact]
    public void DispersionCopiesAllCoefficientRowsAndSupportsEightTerms()
    {
        var k = new double[] { .2, .01, .001, .0001, 1e-5, 1e-6, 1e-7, 1e-8 };
        var zero = new double[8];
        var rows = new IReadOnlyList<double>[] { k, zero, zero };
        var dispersion = new Gradient5Dispersion(550, 400, 700, rows, [[.01], [.02], [.03]]);
        var profile = new Gradient5IndexProfile(1.5, dispersion: dispersion);
        var expected = Math.Sqrt(2.25 + k.Select((v, j) => v * Math.Pow(1.5, j)).Sum() * (.65 * .65 - .55 * .55) / (.65 * .65 - .01));
        k[0] = 100; zero[0] = 99; rows[0] = [50];
        Near(expected, profile.RefractiveIndex(Vector3D.Zero, 650), 1e-15);
        Assert.Equal(8, dispersion.K[0].Count);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)dispersion.K[0])[0] = 20);
    }

    [Theory]
    [InlineData("rows")]
    [InlineData("length")]
    [InlineData("zero")]
    [InlineData("nine")]
    [InlineData("nan")]
    [InlineData("range")]
    [InlineData("reference")]
    public void InvalidCoefficientTablesAndWavelengthDefinitionsAreRejected(string fault)
    {
        IReadOnlyList<IReadOnlyList<double>> k = [[.2], [0], [0]];
        if (fault == "rows") k = [[.2]];
        if (fault == "length") k = [[.2], [0, 0], [0]];
        if (fault == "zero") k = [[], [], []];
        if (fault == "nine") k = [new double[9], new double[9], new double[9]];
        if (fault == "nan") k = [[double.NaN], [0], [0]];
        Assert.ThrowsAny<ArgumentException>(() => new Gradient5Dispersion(fault == "reference" ? 800 : 550,
            fault == "range" ? 0 : 400, 700, k, [[.01], [.02], [.03]]));
    }

    [Theory]
    [InlineData("range")]
    [InlineData("pole")]
    [InlineData("negative")]
    [InlineData("reference-negative")]
    [InlineData("overflow")]
    public void InvalidIndexDomainsFailWithoutFallback(string fault)
    {
        var profile = fault switch
        {
            "pole" => new Gradient5IndexProfile(1.5, dispersion: new(550, 400, 700, [[.2], [0], [0]], [[.25], [.02], [.03]])),
            "negative" => new Gradient5IndexProfile(1.5, dispersion: new(550, 400, 700, [[100], [0], [0]], [[.01], [.02], [.03]])),
            "reference-negative" => new Gradient5IndexProfile(1.5, axial4: -2),
            "overflow" => new Gradient5IndexProfile(1.5, axial4: double.MaxValue),
            _ => Profile(Dispersion())
        };
        Assert.Throws<SpatialIndexDomainException>(() => profile.Evaluate(new(0, 0, 2), fault == "range" ? 750 : 500));
    }

    [Theory]
    [InlineData(450)]
    [InlineData(550)]
    [InlineData(650)]
    public void DispersiveParaxialColumnsConvergeToFormalContinuousRays(double wave)
    {
        var profile = Profile(Dispersion()); var material = Material(profile);
        var matrix = GradientIndexParaxialTransport.Between(material, 0, 8, wave);
        const double epsilon = 1e-5;
        var height = Trace(epsilon, 0); var slope = Trace(0, epsilon);
        Near(matrix.A, height.Position.Y / epsilon, 1e-7);
        Near(matrix.C, height.Direction.Y / height.Direction.Z / epsilon, 1e-7);
        Near(matrix.B, slope.Position.Y / epsilon, 1e-7);
        Near(matrix.D, slope.Direction.Y / slope.Direction.Z / epsilon, 1e-7);
        Near(profile.RefractiveIndex(new(0, 0, 0), wave) / profile.RefractiveIndex(new(0, 0, 8), wave), matrix.Determinant, 2e-10);
        var inverse = GradientIndexParaxialTransport.Between(material, 8, 0, wave);
        Near(1, inverse.A * matrix.A + inverse.B * matrix.C, 2e-10);
        Near(0, inverse.A * matrix.B + inverse.B * matrix.D, 2e-10);
        GradientIndexPathPoint Trace(double h, double u) => GradientIndexRayIntegrator.TraceToSurface(profile,
            CoordinateSystem.Global, new(0, h, 0), new(0, u, 1), wave, new PlaneGeometry(), new(new(0, 0, 8)), 30,
            new(MaximumStep: .02, PositionTolerance: 1e-13, DirectionTolerance: 1e-13, RelativeTolerance: 1e-13)).End;
    }

    [Fact]
    public void QuarticAxialIndexHasAnalyticOpticalPathInTheFormalSequentialTracer()
    {
        var profile = new Gradient5IndexProfile(1.5, axial1: .01, axial2: .002, axial3: -.0001, axial4: .00002);
        var optic = Plate(profile);
        using var trace = optic.SequentialRayTracer.Trace(new([new(new(0, 0, -1), new(0, 0, 1), 550)]),
            TraceRequest.FullHistory() with { NormalizeOpticalPathDifference = false });
        Assert.True(trace.TryGetSample(0, 2, out var sample));
        const double d = 8;
        var expected = 1.5 * d + .01 / 2 * d * d + .002 / 3 * Math.Pow(d, 3) - .0001 / 4 * Math.Pow(d, 4) + .00002 / 5 * Math.Pow(d, 5);
        Near(expected, sample.SegmentOpticalPathLength, 1e-8);
        Near(0, sample.Direction.X, 1e-12); Near(1, sample.Direction.Z, 1e-12);
    }

    [Fact]
    public void AxialDomainRejectsInteriorQuarticNegativeRegionWithPositiveEndpoints()
    {
        // n = (z-1)^4 - .01. Neither endpoint reveals the invalid interior.
        var material = Material(new Gradient5IndexProfile(.99, axial1: -4, axial2: 6, axial3: -4, axial4: 1));
        Assert.Throws<SpatialIndexDomainException>(() => GradientIndexParaxialTransport.Between(material, 0, 2, 550));
    }

    [Fact]
    public void AxialDomainRejectsDispersivePoleEvenIfTheNumeratorVanishes()
    {
        // n_ref=1.5+.1z; L=n_ref/10 crosses λ²=.25 at z=10. A zero K is not a license to erase a pole.
        var profile = new Gradient5IndexProfile(1.5, axial1: .1,
            dispersion: new(550, 400, 700, [[0], [0], [0]], [[0, .1], [.01, 0], [.02, 0]]));
        Assert.Throws<SpatialIndexDomainException>(() => GradientIndexParaxialTransport.Between(Material(profile), 0, 17, 500));
    }

    [Fact]
    public void PositiveQuarticInteriorMinimumAndCancellationAreHandled()
    {
        var material = Material(new Gradient5IndexProfile(1.01, axial1: -4, axial2: 6, axial3: -4, axial4: 1));
        var result = GradientIndexParaxialTransport.Between(material, 0, 2, 550);
        Near(1, result.A, 1e-12); Near(1, result.D, 1e-12); Assert.True(result.B > 2);
        using var source = new CancellationTokenSource(); source.Cancel();
        Assert.Throws<OperationCanceledException>(() => GradientIndexParaxialTransport.Between(material, 0, 2, 550, cancellationToken: source.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispersionProfileAndFormalResultsSurviveStarOptAndApplicationOpen(bool dispersive)
    {
        var profile = Profile(dispersive ? Dispersion() : null); var optic = Plate(profile);
        var snapshot = optic.ToSnapshot(); OpticSnapshotValidator.Validate(snapshot);
        var restored = Optic.FromSnapshot(snapshot);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".staropt");
        try
        {
            await StarOptProjectStore.SaveAsync(new([optic], 0), path);
            using var app = WorkbenchApplication.Create("cooke"); await app.Documents.OpenAsync(path);
            var loaded = (await StarOptProjectStore.LoadAsync(path)).Configurations[0];
            foreach (var candidate in new[] { restored, loaded })
            {
                var actual = Assert.IsType<Gradient5IndexProfile>(Assert.IsType<GradientIndexMaterial>(candidate.SurfaceGroup.Items[1].MaterialAfter).Profile);
                foreach (var wave in new[] { 450.0, 550, 650 })
                {
                    Assert.Equal(profile.Evaluate(new(.2, -.3, 2), wave), actual.Evaluate(new(.2, -.3, 2), wave));
                    Near(optic.Paraxial.EstimateEffectiveFocalLength(), candidate.Paraxial.EstimateEffectiveFocalLength(), 1e-10);
                }
                Assert.Equal(JsonSerializer.Serialize(ComponentSnapshotFactory.FromMaterial(optic.SurfaceGroup.Items[1].MaterialAfter)),
                    JsonSerializer.Serialize(ComponentSnapshotFactory.FromMaterial(candidate.SurfaceGroup.Items[1].MaterialAfter)));
            }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("fractional-count")]
    [InlineData("nine")]
    [InlineData("unknown-child")]
    [InlineData("nested")]
    [InlineData("text")]
    [InlineData("nan")]
    public void MalformedDispersionSnapshotsAreRejectedStrictly(string fault)
    {
        var snapshot = Plate(Profile(Dispersion())).ToSnapshot();
        var material = snapshot.Surfaces[1].Components!.MaterialAfterComponent!;
        var dispersion = material.Children!["dispersion"];
        switch (fault)
        {
            case "missing": dispersion.Numbers.Remove("k1_1"); break;
            case "extra": dispersion.Numbers.Add("unexpected", 0); break;
            case "fractional-count": dispersion.Numbers["kCount"] = 1.5; break;
            case "nine": dispersion.Numbers["lCount"] = 9; break;
            case "unknown-child": material.Children["other"] = dispersion; break;
            case "nested": material.Children["dispersion"] = dispersion with { Children = new() { ["nested"] = dispersion } }; break;
            case "text": dispersion.Text["other"] = "value"; break;
            case "nan": dispersion.Numbers["k1_1"] = double.NaN; break;
        }
        Assert.Throws<InvalidDataException>(() => OpticSnapshotValidator.Validate(snapshot));
        Assert.Throws<InvalidDataException>(() => Optic.FromSnapshot(snapshot));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void SixPointOperandsReadTheRequestedDispersiveWavelength(int point)
    {
        var profile = Profile(Dispersion()); var optic = Plate(profile);
        optic.Wavelengths.Add(new Wavelength { Nanometers = 650, IsPrimary = false });
        foreach (var number in new[] { 1, 2 })
        {
            var row = new MeritOperandDefinition
            {
                Type = $"I{point}VA",
                Surface = 1,
                Wavelength = number,
                ZemaxIntegerParameters = [1, number],
                ZemaxDataParameters = [0, 0, 0, 0]
            };
            var result = MeritFunctionCatalog.Evaluate(optic, row); Assert.Empty(result.Error);
            var position = new Vector3D(point is 3 or 6 ? 1 : 0, point is 2 or 5 ? 1 : 0, point >= 4 ? 8 : 0);
            Assert.Equal(profile.RefractiveIndex(position, number == 1 ? 550 : 650), result.Value);
        }
    }

    [Fact]
    public void HelpDistinguishesModelFoundationFromPendingLptdAndNativeExportIsRejected()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var types = app.Optimization.GetMeritOperandTypes();
        Assert.Contains("Gradient 1～5", types.Single(t => t.Code == "I3VA").Calculation);
        var lptd = types.Single(t => t.Code == "LPTD"); Assert.True(lptd.CompatibilityOnly);
        Assert.Contains("约束残差", lptd.Calculation); Assert.Contains("不能用端点", lptd.Calculation);
        Assert.Throws<NotSupportedException>(() => OpticalFormatCatalog.Export(Plate(Profile(Dispersion())), ".zmx"));
    }

    [Theory]
    [InlineData(450)]
    [InlineData(650)]
    public void AllThreeSellmeierTermsContributeAtTheRequestedWavelength(double wave)
    {
        var profile = new Gradient5IndexProfile(1.5, dispersion: new(550, 400, 700,
            [[.2], [.3], [.4]], [[.01], [.02], [.03]]));
        var w2 = wave * wave / 1e6;
        var expected = Math.Sqrt(2.25 + (w2 - .3025) * (.2 / (w2 - .01) + .3 / (w2 - .02) + .4 / (w2 - .03)));
        Near(expected, profile.RefractiveIndex(Vector3D.Zero, wave), 1e-15);
    }

    [Fact]
    public void DispersiveCacheIdentityChangesAfterMaterialReplacement()
    {
        var first = Plate(Profile(Dispersion())); var second = Optic.FromSnapshot(first.ToSnapshot());
        var cache = new RayTraceCache(8, 1000);
        first.ConfigureRayTraceCache(cache, 123); second.ConfigureRayTraceCache(cache, 123);
        var bundle = new RealRayBundle([new(new(.2, 0, -1), new(0, 0, 1), 650)]);
        using var original = first.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly(false));
        using var hit = second.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([3]));
        Assert.Equal(1, cache.Statistics.Hits);
        Assert.True(original.TryGetSample(0, 3, out var a)); Assert.True(hit.TryGetSample(0, 3, out var b)); Assert.Equal(a, b);
        second.SurfaceGroup.Items[1].MaterialAfter = Material(Profile());
        using var changed = second.SequentialRayTracer.Trace(bundle, TraceRequest.FinalOnly(false));
        Assert.Equal(1, cache.Statistics.Hits); Assert.True(changed.TryGetSample(0, 3, out var c));
        Assert.True(Math.Abs(c.Position.X - a.Position.X) > 1e-6);
    }

    [Fact]
    public void ProductionOptimizationUsesDispersiveQuarticProfileAndFreshEvaluationAgrees()
    {
        var profile = new Gradient5IndexProfile(1.5, axial1: .01, axial4: .000001, dispersion: Dispersion());
        var optic = Plate(profile); optic.Wavelengths.Add(new Wavelength { Nanometers = 650, IsPrimary = false });
        optic.SurfaceGroup.Items[1].ThicknessVariable = true;
        var row = new MeritOperandDefinition
        {
            Type = "I4VA",
            Surface = 1,
            Wavelength = 2,
            Enabled = true,
            Weight = 1,
            ZemaxIntegerParameters = [1, 2],
            ZemaxDataParameters = [0, 0, 0, 0],
            Target = profile.RefractiveIndex(new(0, 0, 6), 650)
        };
        optic.MeritFunctionOperands.Add(row);
        var runtime = new WorkbenchRuntime(optic); var result = runtime.OptimizeMarkedVariables("Damped Least Squares", 40);
        Assert.True(result.FinalMerit < result.InitialMerit * 1e-8);
        Near(6, runtime.CurrentOptic.SurfaceGroup.Items[1].Thickness, 1e-5);
        var fresh = Optic.FromSnapshot(runtime.CurrentOptic.ToSnapshot());
        var actual = MeritFunctionCatalog.Evaluate(fresh, fresh.MeritFunctionOperands.Single());
        Assert.Empty(actual.Error); Near(row.Target, actual.Value, 1e-7);
    }

    private static Gradient5Dispersion Dispersion() => new(550, 400, 700,
        [[.2, .03, .004], [0, 0, 0], [0, 0, 0]], [[.01, .002], [.02, 0], [.03, 0]]);
    private static Gradient5IndexProfile Profile(Gradient5Dispersion? dispersion = null) =>
        new(1.5, -.015, .0002, .01, -.0003, .00002, .000001, dispersion);
    private static GradientIndexMaterial Material(Gradient5IndexProfile p) => new("Gradient 5 test", p, 100,
        new(MaximumStep: .1, PositionTolerance: 1e-12, DirectionTolerance: 1e-12, RelativeTolerance: 1e-12));
    private static Optic Plate(Gradient5IndexProfile p)
    {
        var material = Material(p); var optic = new Optic("Gradient 5");
        optic.Fields.Add(new FieldPoint()); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 8, MaterialAfter = material, IsStop = true, SemiDiameter = 1, SemiDiameterFixed = true },
            new OpticalSurface { Thickness = 5, MaterialBefore = material, SemiDiameter = 1, SemiDiameterFixed = true },
            new OpticalSurface { SemiDiameter = 1, SemiDiameterFixed = true }
        ]);
        return optic;
    }
    private static void Near(double expected, double actual, double tolerance) => Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
}
