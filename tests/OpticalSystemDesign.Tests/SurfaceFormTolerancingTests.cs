using System.Globalization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Tests;

public sealed class SurfaceFormTolerancingTests
{
    [Fact]
    public void TexiUsesDoublePassSagAndAnalyticNormalOnPhysicalClearAperture()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.PhysicalAperture = new CircularAperture(6);
        var nominal = new EvenAsphereGeometry(50, -.1, [.00001]);
        surface.Geometry = nominal;
        var texi = new SurfaceFormPerturbation("TEXI", 1, SurfaceFormMode.FringeZernike,
            1, true, 2, 5, IsoSurfaceFormUnit.Nanometers, .6328);
        var coefficient = .0006328 / 4;

        texi.ApplyMaximum(optic);
        var geometry = surface.Geometry;
        Assert.Equal(-coefficient, geometry.Sag(0, 0) - nominal.Sag(0, 0), 15);
        var sag = geometry.Sag(6, 0);
        var baseSag = nominal.Sag(6, 0);
        Assert.Equal(3 * coefficient, sag - baseSag, 14);
        var nominalNormal = nominal.SurfaceNormal(new Vector3D(6, 0, baseSag));
        var normal = geometry.SurfaceNormal(new Vector3D(6, 0, sag));
        Assert.Equal(-7 * coefficient / 6, normal.X / normal.Z - nominalNormal.X / nominalNormal.Z, 14);
        Assert.Equal(-coefficient / 6, normal.Y / normal.Z, 14);
        var hit = geometry.DistanceToIntersection(new Vector3D(6, 0, -2), new Vector3D(0, 0, 1));
        Assert.True(hit.IsHit);
        Assert.Equal(sag + 2, hit.Distance, 12);

        texi.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
        texi.ApplyScaledMinimum(optic, .25);
        Assert.Equal(-.75 * coefficient, surface.Geometry.Sag(6, 0) - baseSag, 14);
        texi.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
    }

    [Fact]
    public void TexiAddsToExistingFringeTermsAtTheirOriginalNormalization()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.PhysicalAperture = new CircularAperture(9);
        var nominal = new FringeSagGeometry(50, 0, [], 4, [.0002, -.0001], 1, -1);
        surface.Geometry = nominal;
        var texi = new SurfaceFormPerturbation("TEXI", 1, SurfaceFormMode.FringeZernike,
            1, true, 4, 4, IsoSurfaceFormUnit.Nanometers, .6328);

        texi.ApplyMaximum(optic);
        Assert.Equal(.0003164, surface.Geometry.Sag(5, -1) - nominal.Sag(5, -1), 14);
        Assert.Equal(-.0003164, surface.Geometry.Sag(1, -1) - nominal.Sag(1, -1), 14);
        Assert.Equal(nominal.Sag(6, -1), surface.Geometry.Sag(6, -1), 14);
        texi.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TeziUsesExactStandardRmsForEndpointsAndMonteCarlo(bool normalDistribution)
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Radius = 0;
        surface.PhysicalAperture = new CircularAperture(6);
        const double rms = .004;
        var tezi = new SurfaceFormPerturbation("TEZI", 1, SurfaceFormMode.StandardZernike,
            rms, normalDistribution, 2, 6, IsoSurfaceFormUnit.Nanometers, .6328);

        tezi.ApplyMaximum(optic);
        Assert.Equal(rms, DiskRms(surface.Geometry, 6), 14);
        Assert.Equal(-rms * Math.Sqrt(3.0 / 5), surface.Geometry.Sag(0, 0), 14);
        tezi.Revert(optic);
        tezi.ApplyScaledMinimum(optic, .4);
        Assert.Equal(.4 * rms, DiskRms(surface.Geometry, 6), 14);
        tezi.Revert(optic);

        var random = new Random(9210);
        var centerSag = new List<double>();
        for (var trial = 0; trial < 20; trial++)
        {
            tezi.Apply(optic, random);
            Assert.Equal(rms, DiskRms(surface.Geometry, 6), 14);
            centerSag.Add(surface.Geometry.Sag(0, 0));
            tezi.Revert(optic);
        }
        Assert.Contains(centerSag, sag => sag < 0);
        Assert.Contains(centerSag, sag => sag > 0);
        Assert.Equal(20, centerSag.Distinct().Count());
    }

    [Fact]
    public void TeziHandlesRadialOrderTwentyAtTheAxisAndClearRim()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Radius = 0;
        surface.PhysicalAperture = new CircularAperture(6);
        var tezi = new SurfaceFormPerturbation("TEZI", 1, SurfaceFormMode.StandardZernike,
            .004, true, 211, 211, IsoSurfaceFormUnit.Nanometers, .6328);

        tezi.ApplyMaximum(optic);
        // Noll 211 is sqrt(21) P10(2 rho^2 - 1), with P10'(1) = 55.
        var edgeSag = .004 * Math.Sqrt(21);
        var geometry = surface.Geometry;
        Assert.Equal(edgeSag, geometry.Sag(0, 0), 14);
        Assert.Equal(edgeSag, geometry.Sag(6, 0), 14);
        var normal = geometry.SurfaceNormal(new Vector3D(6, 0, edgeSag));
        Assert.Equal(-220 * edgeSag / 6, normal.X / normal.Z, 13);
        Assert.Equal(0, normal.Y, 14);
    }

    [Theory]
    [InlineData(IsoSurfaceFormUnit.Nanometers, 632.8)]
    [InlineData(IsoSurfaceFormUnit.Fringes, 2)]
    [InlineData(IsoSurfaceFormUnit.Micrometers, .6328)]
    public void IsoaUnitsAndTruncatedGaussianAgreeWithSagPowerDefinition(IsoSurfaceFormUnit units, double maximum)
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.PhysicalAperture = new CircularAperture(6);
        var nominal = surface.Geometry;
        var isoa = new SurfaceFormPerturbation("ISOA", 1, SurfaceFormMode.IsoPower, maximum, false, 0, 0, units, .6328);
        const double expectedPv = .0006328;

        isoa.ApplyMaximum(optic);
        Assert.Equal(0, surface.Geometry.Sag(0, 0) - nominal.Sag(0, 0), 15);
        Assert.Equal(expectedPv, surface.Geometry.Sag(6, 0) - nominal.Sag(6, 0), 14);
        Assert.Equal(expectedPv / 4, surface.Geometry.Sag(0, 3) - nominal.Sag(0, 3), 14);
        isoa.Revert(optic);
        Assert.Same(nominal, surface.Geometry);

        var random = new Random(29111);
        var samples = new double[2048];
        for (var i = 0; i < samples.Length; i++)
        {
            isoa.Apply(optic, random);
            samples[i] = (surface.Geometry.Sag(6, 0) - nominal.Sag(6, 0)) / expectedPv;
            Assert.InRange(samples[i], -.999999999, .999999999);
            isoa.Revert(optic);
        }
        Assert.InRange(samples.Average(), -.035, .035);
        Assert.InRange(Math.Sqrt(samples.Average(sample => sample * sample)), .41, .47);
    }

    [Fact]
    public async Task SurfaceFormsFlowThroughMtfSensitivityAndSeededWorkersWithoutChangingThePrescription()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        var request = new TolerancingRequestDto(1, 0, 0, 4, 42, 1,
            [new(1, true, ToleranceOperandKind.FringeZernikeIrregularity, 1, -3, 3),
             new(2, true, ToleranceOperandKind.StandardZernikeIrregularity, 2, -.008, .008),
             new(3, true, ToleranceOperandKind.IsoPower, 3, -800, 800),
             new(4, true, ToleranceOperandKind.Radius, 1, -.02, .02),
             new(5, true, ToleranceOperandKind.Compensator, before.Count - 2, -.1, .1)],
            ToleranceCriterion.Mtf, MaxDegreeOfParallelism: 1,
            MtfSettings: new(20, Method: ToleranceMtfMethod.Geometric));

        Assert.True(app.Tolerancing.ValidateStudy(request).IsValid);
        var serial = await app.Tolerancing.RunAsync(request);
        var parallel = await app.Tolerancing.RunAsync(request with { MaxDegreeOfParallelism = 2 });
        Assert.Equal(4, serial.SensitivityRows.Count);
        Assert.All(serial.SensitivityRows.Take(3), row =>
        {
            Assert.NotEqual("0", row.DeltaMerit);
            Assert.All(row.NegativeFields!.Concat(row.PositiveFields!), field => Assert.Empty(field.Error));
        });
        Assert.Equal(4, serial.TrialRows.Count);
        Assert.Equal(serial.TrialRows.Select(row => row.CriterionValue), parallel.TrialRows.Select(row => row.CriterionValue));
        Assert.Equal(serial.TrialRows.Select(row => row.CompensatedMerit),
            parallel.TrialRows.Select(row => row.CompensatedMerit));
        Assert.Equal(4, serial.TrialRows.Select(row => row.CriterionValue).Distinct().Count());
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(before, app.Prescription.GetSurfaces());
    }

    [Fact]
    public async Task InverseSurfaceFormReusesTheStricterSymmetricLimitAndAcceptsZero()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var operand = new ToleranceOperandDto(1, true, ToleranceOperandKind.StandardZernikeIrregularity,
            1, -.05, .05, MinimumZernikeTerm: 4, MaximumZernikeTerm: 4);
        var request = new TolerancingRequestDto(1, 0, 0, 1, 13, 0, [operand], MaxDegreeOfParallelism: 1,
            Mode: ToleranceAnalysisMode.InverseIncrement, InverseValue: 1e-10);
        var result = await app.Tolerancing.RunAsync(request);
        var endpoints = Assert.Single(result.InverseRows!);
        var saved = Assert.Single(result.AdjustedOperands!);
        Assert.Equal(Math.Min(-double.Parse(endpoints.Minimum.AdjustedTolerance, CultureInfo.InvariantCulture),
            double.Parse(endpoints.Maximum.AdjustedTolerance, CultureInfo.InvariantCulture)), saved.Maximum, 12);
        Assert.Equal(-saved.Maximum, saved.Minimum);
        Assert.InRange(saved.Maximum, 0, .05);
        Assert.Single(result.TrialRows);
        Assert.True(app.Tolerancing.ValidateOperands([saved with { Minimum = 0, Maximum = 0 }]).IsValid);
        var zeroResult = await app.Tolerancing.RunAsync(request with
        {
            Mode = ToleranceAnalysisMode.Sensitivity,
            Operands = [saved with { Minimum = 0, Maximum = 0 }]
        });
        Assert.Equal("0", Assert.Single(zeroResult.SensitivityRows).DeltaMerit);
    }

    [Fact]
    public void SurfaceFormValidationRejectsInvalidTermsAndCompetingShapeSpecifications()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var texi = new ToleranceOperandDto(1, true, ToleranceOperandKind.FringeZernikeIrregularity, 1, -1, 1);
        var tezi = texi with { Index = 2, Kind = ToleranceOperandKind.StandardZernikeIrregularity, MaximumZernikeTerm = 231 };
        var isoa = texi with { Index = 3, Kind = ToleranceOperandKind.IsoPower, IsoUnit = IsoSurfaceFormUnit.Fringes };
        Assert.True(app.Tolerancing.ValidateOperands([texi, isoa]).IsValid);
        Assert.True(app.Tolerancing.ValidateOperands([tezi]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([texi, tezi]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([texi with { Minimum = -.5 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([texi with { MinimumZernikeTerm = 1 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([texi with { MaximumZernikeTerm = 38 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([tezi with { MaximumZernikeTerm = 232 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([isoa with { TestWavelengthMicrometers = 0 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([isoa with { Distribution = ToleranceDistribution.Uniform }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([isoa, texi with { Kind = ToleranceOperandKind.Radius }]).IsValid);
    }

    [Fact]
    public void CoreSerialTrialComposesSurfaceFormWithCurvatureAndRestoresSnapshot()
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Radius = 50;
        surface.PhysicalAperture = new CircularAperture(6);
        var nominal = optic.ToSnapshot();
        var tolerance = optic.CreateTolerancing();
        const double rms = .002;
        tolerance.AddPerturbation(new SurfaceFormPerturbation("TEZI", 1, SurfaceFormMode.StandardZernike,
            rms, false, 4, 4, IsoSurfaceFormUnit.Nanometers, .6328));
        var radius = new DelegateVariable("radius", () => optic.SurfaceGroup.Items[1].Radius,
            value => optic.SurfaceGroup.Items[1].Radius = value, 20, 70, .01);
        tolerance.AddPerturbation(new VariableRangePerturbation("radius", radius, 2, 2, false));
        tolerance.SetCriterionEvaluator(() =>
        {
            var geometry = optic.SurfaceGroup.Items[1].Geometry;
            var centerSag = geometry.Sag(0, 0);
            Assert.Equal(rms * Math.Sqrt(3), Math.Abs(centerSag), 14);
            // At r/a = 1/2 the defocus sag is half the on-axis value.
            return geometry.Sag(3, 0) - centerSag / 2;
        });

        var results = new MonteCarlo(optic, tolerance).RunDetailed(2, 17);
        Assert.Equal(2, results.Count);
        var expectedSag = 9 / (52 + Math.Sqrt(52 * 52 - 9));
        Assert.All(results, result => Assert.Equal(expectedSag, result.Criterion, 14));
        Assert.Equal(nominal.Surfaces[1].Radius, optic.SurfaceGroup.Items[1].Radius);
        Assert.IsType<StandardGeometry>(optic.SurfaceGroup.Items[1].Geometry);
    }

    [Fact]
    public void MechanicalPivotMatchesIndependentRigidTransformAndPreservesInternalPose()
    {
        var first = new CoordinateSystem(new(2, 5, 10));
        var last = new CoordinateSystem(new(3, 7, 19));
        var pivot = new Vector3D(1, -2, 5);
        var rotated = WorkbenchRuntime.KeepMechanicalPivot(first, first with { RotationXDegrees = 90 }, pivot);
        var movedLast = last.MoveWithFrame(first, rotated);

        // Pivot is at (3, 3, 15). Under a +90° X rotation, (x, y, z) becomes (x, -z, y).
        Assert.InRange((rotated.Origin - new Vector3D(2, 8, 17)).Length, 0, 2e-14);
        Assert.InRange((movedLast.Origin - new Vector3D(3, -1, 19)).Length, 0, 2e-14);
        Assert.InRange((movedLast.ToGlobalDirection(new Vector3D(0, 0, 1)) - new Vector3D(0, -1, 0)).Length, 0, 2e-14);
        Assert.InRange((rotated.ToGlobalPoint(pivot) - first.ToGlobalPoint(pivot)).Length, 0, 2e-14);

        var mixed = WorkbenchRuntime.KeepMechanicalPivot(rotated, rotated with { RotationYDegrees = -22 }, pivot);
        var reversed = WorkbenchRuntime.KeepMechanicalPivot(mixed, mixed with { RotationXDegrees = 0, RotationYDegrees = 0 }, pivot);
        Assert.InRange((mixed.ToGlobalPoint(pivot) - first.ToGlobalPoint(pivot)).Length, 0, 2e-14);
        Assert.InRange((reversed.Origin - first.Origin).Length, 0, 2e-14);
        Assert.Equal(first, WorkbenchRuntime.KeepMechanicalPivot(first, first, pivot));
        Assert.Equal(first with { RotationYDegrees = 11 },
            WorkbenchRuntime.KeepMechanicalPivot(first, first with { RotationYDegrees = 11 }, Vector3D.Zero));
    }

    [Fact]
    public async Task GroupCompensationSharesMechanicalPivotAndIsSeedStableAcrossWorkers()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        var pivot = new TolerancePivotDto(1.2, -.7, 9);
        ToleranceCompensatorDto[] compensators =
        [
            new(1, ToleranceCompensatorKind.ElementDecenterX, -.1, .1, 2),
            new(1, ToleranceCompensatorKind.ElementTiltY, -.1, .1, 2, pivot),
            new(1, ToleranceCompensatorKind.ElementTiltX, -.1, .1, 2)
        ];
        var request = new TolerancingRequestDto(1, 0, 0, 3, 8, 4,
            [new(1, true, ToleranceOperandKind.ElementDecenterX, 1, .18, .2, EndSurfaceNumber: 2),
             new(2, true, ToleranceOperandKind.ElementTiltY, 1, .18, .2, EndSurfaceNumber: 2)],
            MaxDegreeOfParallelism: 1, Mode: ToleranceAnalysisMode.SkipSensitivity,
            AdditionalCompensators: compensators);
        Assert.True(app.Tolerancing.ValidateStudy(request).IsValid);
        var serial = await app.Tolerancing.RunAsync(request);
        var parallel = await app.Tolerancing.RunAsync(request with { MaxDegreeOfParallelism = 2 });
        var vertex = await app.Tolerancing.RunAsync(request with
        {
            AdditionalCompensators = compensators.Select(item => item with { Pivot = null }).ToArray()
        });
        Assert.Equal(3, serial.TrialRows.Count);
        for (var i = 0; i < serial.TrialRows.Count; i++)
        {
            var trial = serial.TrialRows[i];
            Assert.Equal(trial.CriterionValue, parallel.TrialRows[i].CriterionValue);
            Assert.Equal(trial.Compensators, parallel.TrialRows[i].Compensators);
            Assert.NotEqual(trial.CompensatedMerit, vertex.TrialRows[i].CompensatedMerit);
            Assert.All(trial.Compensators!, item => Assert.InRange(item.Value, item.Minimum, item.Maximum));
        }
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(before, app.Prescription.GetSurfaces());
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            AdditionalCompensators = [compensators[0] with { Pivot = pivot }]
        }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            AdditionalCompensators = [compensators[1] with { Pivot = pivot with { Z = double.NaN } }]
        }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            AdditionalCompensators =
            [
                compensators[1],
                compensators[2] with { Pivot = pivot with { Z = 8 } }
            ]
        }).IsValid);
    }

    private static double DiskRms(IGeometry geometry, double radius)
    {
        // 3-point Gauss-Legendre in rho^2 plus periodic integration in azimuth is exact for terms 2-6.
        var nodes = new[] { (1 - Math.Sqrt(.6)) / 2, .5, (1 + Math.Sqrt(.6)) / 2 };
        var weights = new[] { 5.0 / 18, 4.0 / 9, 5.0 / 18 };
        var squared = 0.0;
        for (var i = 0; i < nodes.Length; i++)
        for (var j = 0; j < 12; j++)
        {
            var (sine, cosine) = Math.SinCos(j * Math.PI / 6);
            var r = radius * Math.Sqrt(nodes[i]);
            var sag = geometry.Sag(r * cosine, r * sine);
            squared += weights[i] * sag * sag / 12;
        }
        return Math.Sqrt(squared);
    }
}
