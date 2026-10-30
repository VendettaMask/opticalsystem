using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class CementedTopologyTests
{
    [Fact]
    public void SharedGlassInterfaceIsOnePhysicalSurfaceAndMatchesIndependentCoreAssembly()
    {
        var source = TopologyCompiler.Compile(Example());
        var optic = source.CreateOptic();
        var common = source.SurfaceIndices["common"];
        Assert.Equal(7, optic.SurfaceGroup.Items.Count);
        Assert.Equal(source.Elements[0].BackIndex, source.Elements[1].FrontIndex);
        Assert.Equal("N-BK7", optic.SurfaceGroup.Items[common].MaterialBefore.Name);
        Assert.Equal("N-F2", optic.SurfaceGroup.Items[common].MaterialAfter.Name);
        Assert.Equal(2, source.Materials.Length);
        var manual = new Optic("Independent three-interface doublet");
        manual.SurfaceGroup.ImportLegacySurfaces([
            new() { Label = "Object", Thickness = double.PositiveInfinity },
            new() { Label = "Reference", Thickness = 1 },
            new() { Label = "Stop", Thickness = 2, IsStop = true, SemiDiameter = 2 },
            new() { Label = "Front", Radius = 40, Thickness = 3, Material = "N-BK7", SemiDiameter = 5 },
            new() { Label = "Glass/Glass", Radius = -30, Thickness = 2, Material = "N-F2", SemiDiameter = 5 },
            new() { Label = "Back", Radius = -80, Thickness = 47, SemiDiameter = 5 },
            new() { Label = "Image" }
        ], manual.Materials);
        foreach (var wave in source.Topology.Wavelengths)
        {
            var actual = optic.Paraxial.GetTransferMatrix(1, 6, wave.Nanometers);
            var expected = manual.Paraxial.GetTransferMatrix(1, 6, wave.Nanometers);
            Assert.Equal(expected.A, actual.A, 13); Assert.Equal(expected.B, actual.B, 13);
            Assert.Equal(expected.C, actual.C, 13); Assert.Equal(expected.D, actual.D, 13);
        }
        var samples = new[] { new PupilSample(0, 0, 1), new(.4, 0, 1), new(0, -.4, 1) };
        var metric = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, 0, samples, includeSurfaceTransmission: false);
        Assert.NotNull(metric.Metrics);
        Assert.Equal(0, metric.VignettedRayCount);
    }

    [Fact]
    public void ExplicitContactsRoundTripThroughSnapshotAndJsonWithoutCreatingAnAirRegion()
    {
        var source = TopologyCompiler.Compile(Example());
        var imported = TopologyCompiler.Import(source.Snapshot);
        Assert.Single(imported.Topology.CementedInterfaces!.Interfaces);
        Assert.Equal(source.OpticFingerprint, TopologyCompiler.Update(source, source.Snapshot).OpticFingerprint);
        Assert.Equal(source.TopologyFingerprint, ContentFingerprint.Compute(JsonSerializer.Deserialize<DesignTopology>(JsonSerializer.Serialize(source.Topology))!));
        Assert.DoesNotContain(TopologyValidation.AirIntervals(source.Topology), interval => interval.LeftSurfaceId == "common" || interval.RightSurfaceId == "common");
        foreach (var wave in source.Topology.Wavelengths)
        {
            var first = source.CreateOptic().Paraxial.GetTransferMatrix(1, 6, wave.Nanometers);
            var second = imported.CreateOptic().Paraxial.GetTransferMatrix(1, 6, wave.Nanometers);
            Assert.Equal(first.A, second.A, 13); Assert.Equal(first.C, second.C, 13);
        }
    }

    [Theory]
    [InlineData(OpticalDesignParameterKind.Curvature)]
    [InlineData(OpticalDesignParameterKind.AxialPosition)]
    public void OneSharedVariableUpdatesBothOwnersAndCoreDerivativesMatchFiniteDifferences(OpticalDesignParameterKind kind)
    {
        var source = TopologyCompiler.Compile(Example());
        var binding = TopologyParameterBinder.Bind(source, [new("common", kind, kind == OpticalDesignParameterKind.Curvature ? -.1 : 5,
            kind == OpticalDesignParameterKind.Curvature ? .1 : 7)]);
        var initial = binding.InitialCoordinates.Single();
        var derivative = DifferentiableParaxialMetrics.TransferMatrix(source.CreateOptic(), binding.CoreParameters, 1, 6);
        var h = kind == OpticalDesignParameterKind.Curvature ? 1e-6 : 1e-4;
        var plus = TopologyParameterBinder.Apply(source, binding, [initial + h]);
        var minus = TopologyParameterBinder.Apply(source, binding, [initial - h]);
        var mPlus = plus.CreateOptic().Paraxial.GetTransferMatrix(1, 6);
        var mMinus = minus.CreateOptic().Paraxial.GetTransferMatrix(1, 6);
        Assert.InRange(Math.Abs(derivative.A.Derivative(0) - (mPlus.A - mMinus.A) / (2 * h)), 0, 2e-8);
        Assert.InRange(Math.Abs(derivative.C.Derivative(0) - (mPlus.C - mMinus.C) / (2 * h)), 0, 2e-9);
        Assert.Equal(plus.Topology.Elements[0].BackZMillimeters, plus.Topology.Elements[1].FrontZMillimeters);
        Assert.Equal(ContentFingerprint.Compute(plus.Topology.Elements[0].Back), ContentFingerprint.Compute(plus.Topology.Elements[1].Front));
        Assert.Equal(source.Topology.Stop, plus.Topology.Stop);
        Assert.Equal(source.Topology.Image, plus.Topology.Image);
        Assert.Equal(source.Topology.Elements[0].FrontZMillimeters, plus.Topology.Elements[0].FrontZMillimeters);
        Assert.Equal(source.Topology.Elements[1].BackZMillimeters, plus.Topology.Elements[1].BackZMillimeters, 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EitherProtectedOwnerBlocksSharedShapeAndAxialBinding(int owner)
    {
        var model = Example();
        model = model with { Elements = model.Elements.SetItem(owner, model.Elements[owner] with { Protected = true }) };
        var source = TopologyCompiler.Compile(model);
        foreach (var kind in new[] { OpticalDesignParameterKind.Curvature, OpticalDesignParameterKind.AxialPosition })
            Assert.Equal(TopologyFailureCode.ProtectedElement, Assert.Throws<TopologyValidationException>(() =>
                TopologyParameterBinder.Bind(source, [new("common", kind, -10, 10)])).Code);
        var changed = OpticalDesignParameters.Apply(source.Snapshot, [new(source.SurfaceIndices["common"], OpticalDesignParameterKind.Curvature)], [-.032]);
        Assert.Equal(TopologyFailureCode.ProtectedElement, Assert.Throws<TopologyValidationException>(() => TopologyCompiler.Update(source, changed.ToSnapshot())).Code);
    }

    [Theory]
    [InlineData("missing-declaration")]
    [InlineData("gap")]
    [InlineData("shape")]
    [InlineData("aperture")]
    [InlineData("mechanical")]
    [InlineData("version")]
    [InlineData("duplicate")]
    public void InconsistentSharedDeclarationsCannotCompile(string defect)
    {
        var model = Example(); var right = model.Elements[1];
        model = defect switch
        {
            "missing-declaration" => model with { CementedInterfaces = null },
            "gap" => model with { Elements = model.Elements.SetItem(1, right with { FrontZMillimeters = 6.001 }) },
            "shape" => model with { Elements = model.Elements.SetItem(1, right with { Front = right.Front with { Profile = right.Front.Profile with { CurvaturePerMillimeter = -.032 } } }) },
            "aperture" => model with { Elements = model.Elements.SetItem(1, right with { Front = right.Front with { ClearSemiDiameterMillimeters = 4.9 } }) },
            "mechanical" => model with { Elements = model.Elements.SetItem(1, right with { Front = right.Front with { MechanicalSemiDiameterMillimeters = 6 } }) },
            "version" => model with { CementedInterfaces = model.CementedInterfaces! with { Version = 2 } },
            _ => model with { CementedInterfaces = new() { Interfaces = [model.CementedInterfaces!.Interfaces[0], model.CementedInterfaces.Interfaces[0]] } }
        };
        Assert.False(TopologyCompiler.TryCompile(model).Succeeded);
    }

    [Fact]
    public void OrdinaryCpuAcceptanceRetainsBothGlassManufacturingChecksAndOriginalSampling()
    {
        var source = TopologyCompiler.Compile(Example());
        var optic = source.CreateOptic();
        var targets = new TopologyTargets
        {
            EffectiveFocalLengthMillimeters = optic.Paraxial.EstimateEffectiveFocalLength(),
            FNumber = optic.Paraxial.EstimateFNumber()
        };
        var result = TopologyValidator.Validate(source, targets);
        Assert.True(result.Completed, result.Failure);
        Assert.Equal(21, result.Fields.Length);
        Assert.All(result.Fields, field => Assert.Equal(960, field.AttemptedRays));
        Assert.True(result.TracedRayCount > 0); Assert.True(result.GeometryProbes > 0);
        Assert.DoesNotContain(result.Violations, violation => violation.Code.StartsWith("geometry:air:4:4", StringComparison.Ordinal));
        var badElement = source.Topology.Elements[1] with
        {
            ThicknessMillimeters = .2,
            Back = source.Topology.Elements[1].Back with { Profile = new() { CurvaturePerMillimeter = -.1 } }
        };
        var bad = source.Topology with { Elements = source.Topology.Elements.SetItem(1, badElement) };
        var failed = TopologyValidator.Validate(TopologyCompiler.Compile(bad), targets);
        Assert.True(failed.Completed);
        Assert.Contains(failed.Violations, violation => violation.Code.StartsWith("geometry:flint:edge:", StringComparison.Ordinal));
        Assert.False(failed.MeetsTargets);
    }

    [Fact]
    public void RealRayObjectiveUsesTheSameSharedVariableAndMatchesFiniteDifferenceGradient()
    {
        var source = TopologyCompiler.Compile(Example());
        var binding = TopologyParameterBinder.Bind(source, [new("common", OpticalDesignParameterKind.Curvature, -.1, .1, .01)]);
        var objective = new FixedTopologyObjective(source, binding, new(), new()
        { FieldsPerDirection = 3, PupilRings = 1, PupilArms = 4, ProfileRadialSamples = 8 });
        Assert.Equal(objective.ResidualIds.Length, objective.ResidualIds.Distinct(StringComparer.Ordinal).Count());
        var evaluation = objective.Evaluate(binding.InitialCoordinates, includeJacobian: true);
        Assert.True(evaluation.Available, evaluation.Failure);
        Assert.True(evaluation.TracedRayCount > 0);
        Assert.Single(evaluation.Gradient);
        const double h = 1e-5;
        var plus = objective.Evaluate([binding.InitialCoordinates[0] + h]);
        var minus = objective.Evaluate([binding.InitialCoordinates[0] - h]);
        Assert.True(plus.Available, plus.Failure); Assert.True(minus.Available, minus.Failure);
        var fd = (plus.Objective!.Value - minus.Objective!.Value) / (2 * h);
        Assert.InRange(Math.Abs(evaluation.Gradient[0] - fd) / Math.Max(1, Math.Abs(fd)), 0, 2e-5);
    }

    [Fact]
    public void UnversionedStructureEditsAndAutomaticSchedulingRejectCementedGroupsExplicitly()
    {
        var model = Example();
        Assert.Equal(TopologyFailureCode.UnsupportedFeature, TopologyOperators.Delete(model, "flint").Failure!.Code);
        Assert.Throws<NotSupportedException>(() => AutonomousSearchService.Create("cemented-not-yet-scheduled", new() { Seeds = [model] }));
    }

    [Fact]
    public void NullContactOptionKeepsOriginalSerializedShape()
    {
        var plain = new DesignTopology();
        Assert.DoesNotContain("cementedInterfaces", JsonSerializer.Serialize(plain, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    internal static DesignTopology Example()
    {
        var shared = new TopologyFace { ClearSemiDiameterMillimeters = 5, Profile = new() { CurvaturePerMillimeter = -1d / 30 } };
        return new()
        {
            Name = "Authored cemented fixture",
            Stop = new("stop", 1, 2),
            Image = new("image", 55),
            Fields = [new("Axis", 0, 0), new("Edge", 0, 3)],
            Elements = [
                new() { Id = "crown", FrontSurfaceId = "front", BackSurfaceId = "common", FrontZMillimeters = 3, ThicknessMillimeters = 3,
                    Material = "N-BK7", Front = new() { ClearSemiDiameterMillimeters = 5, Profile = new() { CurvaturePerMillimeter = 1d / 40 } }, Back = shared },
                new() { Id = "flint", FrontSurfaceId = "common", BackSurfaceId = "back", FrontZMillimeters = 6, ThicknessMillimeters = 2,
                    Material = "N-F2", Front = shared, Back = new() { ClearSemiDiameterMillimeters = 5, Profile = new() { CurvaturePerMillimeter = -1d / 80 } } }
            ],
            CementedInterfaces = new() { Interfaces = [new("crown", "flint", "common")] }
        };
    }
}
