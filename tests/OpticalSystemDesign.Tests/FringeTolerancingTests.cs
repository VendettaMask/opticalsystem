using System.Globalization;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.Tests;

public sealed class FringeTolerancingTests
{
    [Theory]
    [InlineData(50)]
    [InlineData(0)]
    [InlineData(-50)]
    public void PowerFringeUsesThePhysicalClearRadiusAndRestoresOriginalSurface(double radius)
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.Radius = radius;
        surface.PhysicalAperture = new CircularAperture(6);
        var nominal = surface.Geometry;
        var power = new SurfaceFringePerturbation("TFRN", 1, SurfaceFringeMode.Power, .6328, -1, 1, false);
        var curvature = radius == 0 ? 0 : 1 / radius;

        power.ApplyMaximum(optic);
        Assert.IsType<StandardGeometry>(surface.Geometry);
        Assert.Equal(curvature + .0006328 / 36, 1 / surface.Radius, 14);
        power.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
        power.ApplyScaledMinimum(optic, .5);
        Assert.Equal(curvature - .0006328 / 72, 1 / surface.Radius, 14);
        power.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PowerAndIrregularityComposeAndRestoreInEitherOrder(bool irregularityFirst)
    {
        var optic = Optic.CreateCookeTriplet();
        var surface = optic.SurfaceGroup.Items[1];
        surface.PhysicalAperture = new CircularAperture(6);
        var nominal = surface.Geometry;
        var power = new SurfaceFringePerturbation("TFRN", 1, SurfaceFringeMode.Power, .6328, -1, 1, false);
        var irregularity = new SurfaceFringePerturbation("TIRR", 1, SurfaceFringeMode.Irregularity, .6328, -.2, .2, false);
        var first = irregularityFirst ? irregularity : power;
        var second = irregularityFirst ? power : irregularity;

        first.ApplyMaximum(optic);
        second.ApplyMaximum(optic);
        var geometry = Assert.IsType<IrregularGeometry>(surface.Geometry);
        var coefficient = .0006328 * .2 / 4;
        Assert.Equal(coefficient, geometry.Spherical, 15);
        Assert.Equal(coefficient, geometry.Astigmatism, 15);
        Assert.Equal(0, geometry.AngleDegrees);
        Assert.Equal(1 / 22.01359 + .0006328 / 36, 1 / geometry.Base.Radius, 14);
        Assert.Equal(coefficient, geometry.Sag(6, 0) - geometry.Base.Sag(6, 0), 12);
        Assert.Equal(2 * coefficient, geometry.Sag(0, 6) - geometry.Base.Sag(0, 6), 12);
        Assert.NotEqual(geometry.Base.SurfaceNormal(new(0, 6, geometry.Base.Sag(0, 6))),
            geometry.SurfaceNormal(new(0, 6, geometry.Sag(0, 6))));
        second.Revert(optic);
        first.Revert(optic);
        Assert.Same(nominal, surface.Geometry);

        irregularity.Apply(optic, new Random(314));
        var random = new Random(314);
        var sampled = -.2 + .4 * random.NextDouble();
        var angle = 360 * random.NextDouble();
        var sample = Assert.IsType<IrregularGeometry>(surface.Geometry);
        Assert.Equal(angle, sample.AngleDegrees, 12);
        Assert.Equal(.0006328 * sampled / 4, sample.Spherical, 15);
        irregularity.Revert(optic);
        Assert.Same(nominal, surface.Geometry);
    }

    [Fact]
    public void RigidFrameMovePreservesEveryMemberPointAndNormalUnderCombinedAdjustments()
    {
        var from = new CoordinateSystem(new(1, 2, 3), 8, -12, 3);
        var to = new CoordinateSystem(new(1.1, 1.8, 3), -4, 17, 3);
        var member = new CoordinateSystem(new(2, 1, 8), -7, 5, -12);
        var local = new Vector3D(1, 2, 3);
        var moved = member.MoveWithFrame(from, to);

        Assert.InRange((from.ToLocalPoint(member.ToGlobalPoint(local)) -
            to.ToLocalPoint(moved.ToGlobalPoint(local))).Length, 0, 1e-12);
        Assert.InRange((from.ToLocalDirection(member.ToGlobalDirection(local)) -
            to.ToLocalDirection(moved.ToGlobalDirection(local))).Length, 0, 1e-12);
        Assert.InRange((moved.MoveWithFrame(to, from).ToGlobalPoint(local) -
            member.ToGlobalPoint(local)).Length, 0, 1e-12);
    }

    [Fact]
    public async Task FringeTolerancesChangeTracedMtfAndProduceIdenticalSerialAndParallelTrials()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        var request = new TolerancingRequestDto(1, 0, 0, 4, 42, 0,
            [new(1, true, ToleranceOperandKind.FringeIrregularity, 1, -2, 2, TestWavelengthMicrometers: .5461),
             new(2, true, ToleranceOperandKind.FringePower, 1, -5, 5, TestWavelengthMicrometers: .6328)],
            ToleranceCriterion.Mtf, MaxDegreeOfParallelism: 1,
            MtfSettings: new(20, Method: ToleranceMtfMethod.Geometric));

        var serial = await app.Tolerancing.RunAsync(request);
        var parallel = await app.Tolerancing.RunAsync(request with { MaxDegreeOfParallelism = 2 });
        Assert.Equal(2, serial.SensitivityRows.Count);
        Assert.All(serial.SensitivityRows, row =>
        {
            Assert.NotEqual("0", row.DeltaMerit);
            Assert.All(row.NegativeFields!.Concat(row.PositiveFields!), f =>
            {
                Assert.Empty(f.Error);
                Assert.InRange(f.Value, 0, 1);
            });
        });
        Assert.Equal(4, serial.TrialRows.Count);
        Assert.Equal(serial.TrialRows.Select(row => row.CriterionValue), parallel.TrialRows.Select(row => row.CriterionValue));
        Assert.Equal(4, serial.TrialRows.Select(row => row.CriterionValue).Distinct().Count());
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(before, app.Prescription.GetSurfaces());
    }

    [Fact]
    public void FringeValidationRejectsBadWavelengthAndCurvatureOutsideClearAperture()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var operand = new ToleranceOperandDto(1, true, ToleranceOperandKind.FringePower, 1, -1, 1);
        Assert.True(app.Tolerancing.ValidateOperands([operand]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([operand with { TestWavelengthMicrometers = 0 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([operand with { Maximum = 1e6 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([operand with { SurfaceNumber = 0 }]).IsValid);
        Assert.False(app.Tolerancing.ValidateOperands([operand, operand with { Index = 2, Kind = ToleranceOperandKind.Radius }]).IsValid);
    }

    [Fact]
    public async Task BoundedGroupAdjustmentReducesManufacturedDecenterAndTilt()
    {
        using var app = WorkbenchApplication.Create("cooke");
        var before = app.Prescription.GetSurfaces();
        var revision = app.Events.Revision;
        var request = new TolerancingRequestDto(1, 0, 0, 3, 42, 6,
            [new(1, true, ToleranceOperandKind.ElementDecenterX, 1, .18, .2, EndSurfaceNumber: 2),
             new(2, true, ToleranceOperandKind.ElementTiltY, 1, .18, .2, EndSurfaceNumber: 2)],
            MaxDegreeOfParallelism: 1, Mode: ToleranceAnalysisMode.SkipSensitivity,
            AdditionalCompensators: [
                new(1, ToleranceCompensatorKind.ElementDecenterX, -.1, .1, 2),
                new(1, ToleranceCompensatorKind.ElementTiltY, -.1, .1, 2),
                new(before.Count - 2, ToleranceCompensatorKind.Thickness, -.1, .1)
            ]);

        Assert.True(app.Tolerancing.ValidateStudy(request).IsValid);
        var result = await app.Tolerancing.RunAsync(request);
        Assert.Equal(3, result.TrialRows.Count);
        Assert.All(result.TrialRows, trial =>
        {
            Assert.True(double.Parse(trial.CompensatedMerit, CultureInfo.InvariantCulture)
                < double.Parse(trial.Merit, CultureInfo.InvariantCulture));
            Assert.Equal(3, trial.Compensators!.Count);
            foreach (var compensator in trial.Compensators)
                Assert.InRange(compensator.Value, compensator.Minimum, compensator.Maximum);
            Assert.Contains(trial.Compensators, c => c.Name.Contains("1–2") && c.Value != c.Nominal);
        });
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(before, app.Prescription.GetSurfaces());

        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            AdditionalCompensators = [new(1, ToleranceCompensatorKind.ElementTiltX, -.1, .1, 1)]
        }).IsValid);
        Assert.False(app.Tolerancing.ValidateStudy(request with
        {
            AdditionalCompensators = [new(1, ToleranceCompensatorKind.ElementTiltX, -.1, .1, 2),
                new(2, ToleranceCompensatorKind.DecenterX, -.1, .1)]
        }).IsValid);
    }
}
