using System.Text.Json;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Tests;

public sealed class IsoIrregularityTolerancingTests
{
    [Fact]
    public void ContinuousPeakToValleyIncludesInteriorExtremaAndRepeatedRoots()
    {
        // Q = (t - 0.4)^4 has a flat interior minimum.
        Assert.Equal(Math.Pow(.6, 4), IsoSurfaceFigure.RadialPeakToValley([.0256, -.256, .96, -1.6, 1], 0), 13);
        // With m=2 the envelope is t(1-t); with m=1 it is sqrt(t)(1-t).
        Assert.Equal(.5, IsoSurfaceFigure.RadialPeakToValley([1, -1], 2), 14);
        Assert.Equal(4 / (3 * Math.Sqrt(3)), IsoSurfaceFigure.RadialPeakToValley([1, -1], 1), 14);
    }

    [Theory]
    [InlineData(IsoSurfaceFormUnit.Nanometers, 632.8)]
    [InlineData(IsoSurfaceFormUnit.Micrometers, .6328)]
    [InlineData(IsoSurfaceFormUnit.Fringes, 2)]
    public void IsocMatchesIndependentRadialShapeAndSignedPeakToValley(IsoSurfaceFormUnit unit, double maximum)
    {
        var (optic, surface) = Plane();
        var nominal = surface.Geometry;
        var operand = Make(SurfaceFormMode.IsoRotationalIrregularityPv, maximum, unit);
        const double pv = .0006328;
        operand.ApplyMaximum(optic);
        var positive = surface.Geometry;
        Assert.Equal(0, positive.Sag(0, 0), 14);
        Assert.InRange(DensePeakToValley(positive, 0), pv * (1 - 1e-6), pv * (1 + 1e-11));

        // Endpoint shape is proportional to the four public Standard radial terms.
        var t = .38;
        var r = 6 * Math.Sqrt(t);
        var predictedRatio = IsocEndpointProfile(t) / IsocEndpointProfile(1);
        Assert.Equal(predictedRatio, positive.Sag(r, 0) / positive.Sag(6, 0), 12);
        Assert.Equal(positive.Sag(r, 0), positive.Sag(0, r), 14);
        var normal = positive.SurfaceNormal(new Vector3D(r, 0, positive.Sag(r, 0)));
        const double step = 1e-5;
        Assert.Equal(-(positive.Sag(r + step, 0) - positive.Sag(r - step, 0)) / (2 * step),
            normal.X / normal.Z, 11);
        var hit = positive.DistanceToIntersection(new Vector3D(r, 0, -1), new Vector3D(0, 0, 1));
        Assert.True(hit.IsHit);
        Assert.Equal(1 + positive.Sag(r, 0), hit.Distance, 12);
        operand.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
        operand.ApplyScaledMinimum(optic, .35);
        Assert.Equal(-.35 * positive.Sag(r, 0), surface.Geometry.Sag(r, 0), 14);
        operand.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
    }

    [Fact]
    public void IsocMonteCarloAtMaximumUsesDistinctRadialShapes()
    {
        var (optic, surface) = Plane();
        var operand = Make(SurfaceFormMode.IsoRotationalIrregularityPv, 1200);
        var random = new Random(43);
        var sag = new HashSet<double>();
        for (var trial = 0; trial < 12; trial++)
        {
            operand.Apply(optic, random);
            Assert.InRange(DensePeakToValley(surface.Geometry, 0), .0012 * (1 - 1e-6), .0012 * (1 + 1e-11));
            Assert.Equal(0, surface.Geometry.Sag(0, 0), 14);
            Assert.True(sag.Add(surface.Geometry.Sag(3, 0)));
            operand.Revert(optic);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void IsobEachAngularFamilyMatchesContinuousDiskPeakToValley(int order)
    {
        var (optic, surface) = Plane();
        var operand = Make(SurfaceFormMode.IsoIrregularityPv, 1600);
        var random = new AngularFamilyRandom(order);
        operand.Apply(optic, random);
        const double expected = .0016;
        Assert.InRange(DensePeakToValley(surface.Geometry, order), expected * (1 - 1e-6), expected * (1 + 1e-11));
        Assert.Equal(0, surface.Geometry.Sag(0, 0), 14);
        if (order > 0)
        {
            var theta = Math.PI / order;
            Assert.Equal(-surface.Geometry.Sag(3, 0),
                surface.Geometry.Sag(3 * Math.Cos(theta), 3 * Math.Sin(theta)), 14);
        }
    }

    [Fact]
    public void IsodSeparatesCenteredRmsFromVertexDatumAndUsesPositiveTruncatedGaussian()
    {
        var (optic, surface) = Plane();
        const double expected = .0012;
        var exact = Make(SurfaceFormMode.IsoIrregularityRms, 1.2, IsoSurfaceFormUnit.Micrometers);
        exact.ApplyMaximum(optic);
        Assert.Equal(0, surface.Geometry.Sag(0, 0), 14);
        var (mean, rms, raw) = DiskStatistics(surface.Geometry);
        Assert.Equal(expected, rms, 14);
        Assert.True(raw > rms);
        Assert.Equal(mean * mean + rms * rms, raw * raw, 15);
        exact.Revert(optic);

        var fixedRandom = new Random(19);
        for (var trial = 0; trial < 12; trial++)
        {
            exact.Apply(optic, fixedRandom);
            Assert.Equal(expected, DiskStatistics(surface.Geometry).Rms, 14);
            exact.Revert(optic);
        }

        var sampled = Make(SurfaceFormMode.IsoIrregularityRms, 1.2, IsoSurfaceFormUnit.Micrometers,
            IsoSurfaceFormStatistics.TruncatedGaussian);
        var random = new Random(32910);
        var fractions = new double[384];
        for (var trial = 0; trial < fractions.Length; trial++)
        {
            sampled.Apply(optic, random);
            fractions[trial] = DiskStatistics(surface.Geometry).Rms / expected;
            Assert.InRange(fractions[trial], double.Epsilon, 1 - 1e-14);
            sampled.Revert(optic);
        }
        var center = fractions.Average();
        Assert.InRange(center, .475, .525);
        Assert.InRange(Math.Sqrt(fractions.Average(value => Math.Pow(value - center, 2))), .105, .145);
        Assert.Equal(fractions.Length, fractions.Distinct().Count());
    }

    [Fact]
    public async Task IsoIrregularitiesUseTheRealMtfWorkersAndPreserveTheNominalPrescription()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        var request = new TolerancingRequestDto(1, 0, 0, 4, 82, 0,
            [new(1, true, ToleranceOperandKind.IsoIrregularityPv, 1, -1500, 1500),
             new(2, true, ToleranceOperandKind.IsoRotationalIrregularityPv, 2, -2, 2,
                 IsoUnit: IsoSurfaceFormUnit.Fringes, TestWavelengthMicrometers: .5461,
                 IsoStatistics: IsoSurfaceFormStatistics.TruncatedGaussian),
             new(3, true, ToleranceOperandKind.IsoIrregularityRms, 3, -1.5, 1.5, IsoUnit: IsoSurfaceFormUnit.Micrometers)],
            ToleranceCriterion.Mtf, MaxDegreeOfParallelism: 1,
            MtfSettings: new(20, Method: ToleranceMtfMethod.Geometric));

        Assert.True(app.Tolerancing.ValidateStudy(request).IsValid);
        var single = await app.Tolerancing.RunAsync(request);
        var multi = await app.Tolerancing.RunAsync(request with { MaxDegreeOfParallelism = 2 });
        Assert.Equal(3, single.SensitivityRows.Count);
        Assert.All(single.SensitivityRows, row =>
        {
            Assert.NotEqual("0", row.DeltaMerit);
            Assert.All(row.NegativeFields!.Concat(row.PositiveFields!), field => Assert.Empty(field.Error));
        });
        Assert.Equal(single.TrialRows.Select(trial => trial.CriterionValue), multi.TrialRows.Select(trial => trial.CriterionValue));
        Assert.Equal(4, single.TrialRows.Select(trial => trial.CriterionValue).Distinct().Count());

        var inverse = await app.Tolerancing.RunAsync(request with
        {
            Criterion = ToleranceCriterion.RmsSpotRadius, Mode = ToleranceAnalysisMode.InverseIncrement,
            InverseValue = .00005, Trials = 1
        });
        Assert.Equal(3, inverse.InverseRows!.Count);
        Assert.All(inverse.AdjustedOperands!, row => Assert.Equal(-row.Maximum, row.Minimum));
        Assert.Equal(IsoSurfaceFormStatistics.TruncatedGaussian, inverse.AdjustedOperands![1].IsoStatistics);
        Assert.Single(inverse.TrialRows);
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(before, app.Prescription.GetSurfaces());
    }

    [Fact]
    public void ValidationRejectsCompetingIrregularitiesAndInvalidIsoMetadata()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var b = new ToleranceOperandDto(1, true, ToleranceOperandKind.IsoIrregularityPv, 1, -1, 1);
        var c = b with { Index = 2, Kind = ToleranceOperandKind.IsoRotationalIrregularityPv };
        var d = b with { Index = 3, Kind = ToleranceOperandKind.IsoIrregularityRms };
        Assert.True(app.Tolerancing.ValidateOperands([b, c with { SurfaceNumber = 2 }, d with { SurfaceNumber = 3 }]).IsValid);
        Assert.True(app.Tolerancing.ValidateOperands([c, b with { Kind = ToleranceOperandKind.IsoPower }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b, c]).IsValid);
        Assert.True(app.Tolerancing.ValidateOperands([b, c with { Minimum = -.0006, Maximum = .0006,
            IsoUnit = IsoSurfaceFormUnit.Micrometers }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b, c with { Minimum = -.1, Maximum = .1,
            IsoUnit = IsoSurfaceFormUnit.Fringes }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b, c, d]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b, d]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([c, d]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([d, d with { Index = 9 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([d, b with { Kind = ToleranceOperandKind.FringeIrregularity }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b with { Minimum = -.5 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b with { IsoUnit = (IsoSurfaceFormUnit)20 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([b with { IsoStatistics = (IsoSurfaceFormStatistics)20 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([c with { IsoUnit = IsoSurfaceFormUnit.Fringes, TestWavelengthMicrometers = 0 }]).IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void JointIsoCalibratesTotalAndRotationalSagOnTheContinuousAperture(int harmonic)
    {
        var (optic, surface) = Plane();
        var nominal = surface.Geometry;
        var total = Make(SurfaceFormMode.IsoIrregularityPv, 1600);
        var rotational = Make(SurfaceFormMode.IsoRotationalIrregularityPv, .6, IsoSurfaceFormUnit.Micrometers);
        var tolerancing = optic.CreateTolerancing();
        tolerancing.AddPerturbation(rotational);
        tolerancing.AddPerturbation(total);
        var pair = Assert.IsAssignableFrom<ISampledPerturbation>(Assert.Single(tolerancing.TrialPerturbations()));
        pair.Apply(optic, new HarmonicRandom(harmonic));
        var (b, c) = MeasureJointSag(surface.Geometry, nominal, harmonic);
        Assert.InRange(b, .0016 * (1 - 2e-6), .0016 * (1 + 1e-9));
        Assert.InRange(c, .0006 * (1 - 2e-6), .0006 * (1 + 1e-9));
        Assert.Equal(0, surface.Geometry.Sag(0, 0), 14);

        var point = new Vector3D(3, 1, surface.Geometry.Sag(3, 1));
        var normal = surface.Geometry.SurfaceNormal(point);
        const double step = 1e-5;
        var dx = (surface.Geometry.Sag(3 + step, 1) - surface.Geometry.Sag(3 - step, 1)) / (2 * step);
        Assert.Equal(dx, -normal.X / normal.Z, 9);
        pair.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
    }

    [Theory]
    [InlineData(IsoSurfaceFormStatistics.AtMaximum, IsoSurfaceFormStatistics.TruncatedGaussian)]
    [InlineData(IsoSurfaceFormStatistics.TruncatedGaussian, IsoSurfaceFormStatistics.AtMaximum)]
    [InlineData(IsoSurfaceFormStatistics.TruncatedGaussian, IsoSurfaceFormStatistics.TruncatedGaussian)]
    public void JointSamplingPreservesFeasibleAmplitudes(IsoSurfaceFormStatistics bStatistics,
        IsoSurfaceFormStatistics cStatistics)
    {
        var (optic, surface) = Plane();
        var nominal = surface.Geometry;
        var total = Make(SurfaceFormMode.IsoIrregularityPv, 1000, statistics: bStatistics);
        var rotational = Make(SurfaceFormMode.IsoRotationalIrregularityPv, 990, statistics: cStatistics);
        var pair = total.WithRotationalIrregularity(rotational);
        var random = new HarmonicRandom(3);

        for (var trial = 0; trial < 8; trial++)
        {
            pair.Apply(optic, random);
            var (b, c) = MeasureJointSag(surface.Geometry, nominal, 3);
            Assert.InRange(b, c, .001000000001);
            if (bStatistics == IsoSurfaceFormStatistics.AtMaximum) Assert.InRange(b, .00099999, .001000000001);
            if (cStatistics == IsoSurfaceFormStatistics.AtMaximum) Assert.InRange(c, .00098999, .000990000001);
            if (bStatistics == cStatistics) Assert.InRange(c / b, .98999, .99001);
            pair.Revert(optic);
        }
    }

    [Fact]
    public async Task JointRowsRunAndReverseIndependentlyThenShareOneTrialShape()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var b = new ToleranceOperandDto(1, true, ToleranceOperandKind.IsoIrregularityPv, 1, -1500, 1500);
        var c = new ToleranceOperandDto(2, true, ToleranceOperandKind.IsoRotationalIrregularityPv, 1, -1, 1,
            IsoUnit: IsoSurfaceFormUnit.Micrometers);
        var request = new TolerancingRequestDto(1, 0, 0, 3, 192, 0, [b, c],
            ToleranceCriterion.Mtf, MaxDegreeOfParallelism: 1,
            MtfSettings: new(20, Method: ToleranceMtfMethod.Geometric));

        Assert.True(app.Tolerancing.ValidateStudy(request).IsValid);
        var forward = await app.Tolerancing.RunAsync(request);
        var parallel = await app.Tolerancing.RunAsync(request with { Operands = [c, b], MaxDegreeOfParallelism = 2 });
        Assert.Equal(2, forward.SensitivityRows.Count);
        Assert.All(forward.SensitivityRows, row =>
            Assert.All(row.NegativeFields!.Concat(row.PositiveFields!), field => Assert.Empty(field.Error)));
        Assert.Equal(3, forward.TrialRows.Count);
        Assert.Equal(forward.TrialRows.Select(trial => trial.CriterionValue),
            parallel.TrialRows.Select(trial => trial.CriterionValue));

        var inverse = await app.Tolerancing.RunAsync(request with
        {
            Criterion = ToleranceCriterion.RmsSpotRadius, Mode = ToleranceAnalysisMode.InverseIncrement,
            InverseValue = .00001, Trials = 1
        });
        Assert.Equal(2, inverse.InverseRows!.Count);
        var adjusted = inverse.AdjustedOperands!;
        Assert.True(app.Tolerancing.ValidateOperands(adjusted).IsValid);
        Assert.All(adjusted, row => Assert.Equal(-row.Maximum, row.Minimum));
        Assert.Single(inverse.TrialRows);
        Assert.Equal(before, app.Prescription.GetSurfaces());
    }

    [Fact]
    public void FixedJointIsoMatchesAnExplicitFringeSagZmxAtIdenticalMtfSettings()
    {
        const double radius = 6;
        const double total = .0015375;
        const double rotational = .0006;
        var original = Optic.CreateCookeTriplet();
        original.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(radius);
        original.SurfaceGroup.Items[1].SemiDiameter = radius;
        var exporter = new ZemaxZmxExporter();
        var importer = new ZemaxZmxImporter();
        var nominalText = exporter.Export(original);
        var explicitOptic = importer.Import(nominalText);
        var target = explicitOptic.SurfaceGroup.Items[1];
        var terms = new double[FringeZernikeBasis.Orders.Count];
        for (var i = 0; i < terms.Length; i++)
            terms[i] = FringeZernikeBasis.Orders[i] switch
            {
                (0, 0) => -.0004,
                (4, 0) => .0004,
                (2, 2) => .0006,
                _ => 0
            };
        target.Geometry = new FringeSagGeometry(target.Radius, target.Conic, [], radius, terms, extrapolate: true);
        var perturbedText = exporter.Export(explicitOptic);
        var perturbed = importer.Import(perturbedText);
        var nominal = importer.Import(nominalText);
        var transient = importer.Import(nominalText);
        var perturbation = Make(SurfaceFormMode.IsoIrregularityPv, total * 1e6)
            .WithRotationalIrregularity(Make(SurfaceFormMode.IsoRotationalIrregularityPv, rotational * 1e6));
        perturbation.Apply(transient, new FixedPolynomialRandom());

        var nominalGeometry = nominal.SurfaceGroup.Items[1].Geometry;
        var fixedGeometry = perturbed.SurfaceGroup.Items[1].Geometry;
        var transientGeometry = transient.SurfaceGroup.Items[1].Geometry;
        var (b, c) = MeasureJointSag(fixedGeometry, nominalGeometry, 2);
        Assert.Equal(total, b, 10);
        Assert.Equal(rotational, c, 10);
        foreach (var (x, y) in new[] { (0d, 0d), (2d, 0d), (0d, 4d), (1d, 3d), (0d, 6d), (6d, 0d) })
        {
            Assert.Equal(fixedGeometry.Sag(x, y), transientGeometry.Sag(x, y), 14);
            var first = fixedGeometry.SurfaceNormal(new(x, y, fixedGeometry.Sag(x, y)));
            var second = transientGeometry.SurfaceNormal(new(x, y, transientGeometry.Sag(x, y)));
            Assert.InRange((first - second).Length, 0, 1e-12);
        }

        const double frequency = 20;
        const int wave = 2;
        const int field = 1;
        var comparisons = new List<object>();
        foreach (var sampling in new[] { 3, 4 })
        {
            var before = MtfMetrics.EvaluateGrid(nominal, MtfMetricKind.Fourier, sampling, wave, field, frequency);
            var fixedMtf = MtfMetrics.EvaluateGrid(perturbed, MtfMetricKind.Fourier, sampling, wave, field, frequency);
            var joint = MtfMetrics.EvaluateGrid(transient, MtfMetricKind.Fourier, sampling, wave, field, frequency);
            Assert.InRange(fixedMtf.Tangential, 0, 1);
            Assert.InRange(fixedMtf.Sagittal, 0, 1);
            Assert.Equal(fixedMtf.Tangential, joint.Tangential, 10);
            Assert.Equal(fixedMtf.Sagittal, joint.Sagittal, 10);
            Assert.NotEqual(before, fixedMtf);
            comparisons.Add(new { pupilSampling = 32 << (sampling - 1), nominal = before, fixedFringeSag = fixedMtf, joint });
        }
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_JOINT_ISO_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "nominal.zmx"), nominalText);
            File.WriteAllText(Path.Combine(directory, "fixed-joint-iso.zmx"), perturbedText);
            File.WriteAllText(Path.Combine(directory, "fixed-perturbation-workbench.json"),
                JsonSerializer.Serialize(new
                {
                    source = "Workbench exported Cooke Triplet",
                    nativeOpticStudioCapture = false,
                    surface = 1,
                    normalizationRadiusMm = radius,
                    targetTotalPvMm = total,
                    targetRotationalPvMm = rotational,
                    fringeCoefficientsMm = terms,
                    field, wave, frequencyCyclesPerMm = frequency,
                    method = "FFT MTF, modulation, no refocus",
                    comparisons
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed class FixedPolynomialRandom : Random
    {
        private static readonly int AngularTerms = ZernikeFitEngine.StandardIndices(37).Skip(4)
            .Count(order => order.M == 2);
        private int _sample;
        public override int Next(int minValue, int maxValue) => 2;
        public override double NextDouble()
        {
            var index = _sample++;
            if (index == 4 + AngularTerms) return 0;
            return index is 0 or 4 ? .75 : .5;
        }
    }

    private static (double Total, double Rotational) MeasureJointSag(IGeometry figure, IGeometry nominal,
        int harmonic)
    {
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        var radialMinimum = double.PositiveInfinity;
        var radialMaximum = double.NegativeInfinity;
        const int samples = 8192;
        for (var i = 0; i <= samples; i++)
        {
            var r = 6.0 * i / samples;
            double Sag(int quadrant)
            {
                var (sine, cosine) = Math.SinCos(quadrant * Math.PI / (2 * harmonic));
                var (x, y) = (r * cosine, r * sine);
                return figure.Sag(x, y) - nominal.Sag(x, y);
            }
            var a = Sag(0);
            var b = Sag(1);
            var c = Sag(2);
            var d = Sag(3);
            var radial = (a + b + c + d) / 4;
            var envelope = double.Hypot(a - c, b - d) / 2;
            minimum = Math.Min(minimum, radial - envelope);
            maximum = Math.Max(maximum, radial + envelope);
            radialMinimum = Math.Min(radialMinimum, radial);
            radialMaximum = Math.Max(radialMaximum, radial);
        }
        return (maximum - minimum, radialMaximum - radialMinimum);
    }

    private sealed class HarmonicRandom(int harmonic) : Random(73)
    {
        public override int Next(int minValue, int maxValue) => harmonic;
    }

    private static (Optic Optic, OpticalSurface Surface) Plane()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Radius = 0;
        surface.PhysicalAperture = new CircularAperture(6);
        return (optic, surface);
    }

    private static SurfaceFormPerturbation Make(SurfaceFormMode mode, double maximum,
        IsoSurfaceFormUnit unit = IsoSurfaceFormUnit.Nanometers,
        IsoSurfaceFormStatistics statistics = IsoSurfaceFormStatistics.AtMaximum) =>
        new(mode.ToString(), 1, mode, maximum, false, 0, 0, unit, .6328, statistics);

    private static double IsocEndpointProfile(double t)
    {
        var x = 2 * t - 1;
        return Math.Sqrt(5) * ((3 * x * x - 1) / 2 - 1)
            + Math.Sqrt(7) * ((5 * Math.Pow(x, 3) - 3 * x) / 2 + 1)
            + 3 * ((35 * Math.Pow(x, 4) - 30 * x * x + 3) / 8 - 1)
            + Math.Sqrt(11) * ((63 * Math.Pow(x, 5) - 70 * Math.Pow(x, 3) + 15 * x) / 8 + 1);
    }

    private static double DensePeakToValley(IGeometry geometry, int angularOrder)
    {
        // Independently scan the traced sag on a dense radial grid. Orthogonal
        // angular phases recover the envelope for the single-frequency B family.
        var minimum = 0.0;
        var maximum = 0.0;
        var (sine, cosine) = Math.SinCos(Math.PI / (2 * Math.Max(1, angularOrder)));
        for (var i = 0; i <= 8192; i++)
        {
            var r = 6 * Math.Sqrt(i / 8192.0);
            var x = geometry.Sag(r, 0);
            if (angularOrder == 0)
            {
                minimum = Math.Min(minimum, x);
                maximum = Math.Max(maximum, x);
                continue;
            }
            var y = geometry.Sag(r * cosine, r * sine);
            maximum = Math.Max(maximum, Math.Sqrt(x * x + y * y));
        }
        return angularOrder == 0 ? maximum - minimum : 2 * maximum;
    }

    private static (double Mean, double Rms, double RawRms) DiskStatistics(IGeometry geometry)
    {
        double[] nodes = [-.906179845938664, -.538469310105683, 0, .538469310105683, .906179845938664];
        double[] weights = [.236926885056189, .478628670499366, .568888888888889, .478628670499366, .236926885056189];
        var mean = 0.0;
        var sumSquares = 0.0;
        for (var i = 0; i < nodes.Length; i++)
        {
            var r = 6 * Math.Sqrt((nodes[i] + 1) / 2);
            for (var azimuth = 0; azimuth < 32; azimuth++)
            {
                var (sine, cosine) = Math.SinCos(2 * Math.PI * azimuth / 32);
                var value = geometry.Sag(r * cosine, r * sine);
                var weight = weights[i] / 64;
                mean += weight * value;
                sumSquares += weight * value * value;
            }
        }
        return (mean, Math.Sqrt(sumSquares - mean * mean), Math.Sqrt(sumSquares));
    }

    private sealed class AngularFamilyRandom(int order) : Random(192)
    {
        public override int Next(int maxValue) => order;
    }
}
