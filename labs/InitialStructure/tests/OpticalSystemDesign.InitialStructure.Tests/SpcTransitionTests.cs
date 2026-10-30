using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class SpcTransitionTests
{
    [Theory]
    [InlineData(-.001)]
    [InlineData(0)]
    [InlineData(.001)]
    public void ZeroThicknessNullPairPreservesTheFrozenCoreMatrixAtEveryActiveWavelength(double offset)
    {
        var source = Parent();
        var state = SpcTransitionCompiler.Create(source, "crown", "null-1") with { NullCurvatureOffsetPerMillimeter = offset };
        using var measured = SequentialTraceMeasurement.Begin();
        var compiled = SpcTransitionCompiler.Compile(state);
        Assert.Equal(SpcTransitionState.Identity, compiled.Algorithm);
        var optic = compiled.CreateOptic();
        Assert.Equal(source.CreateOptic().SurfaceGroup.Items.Count + 2, optic.SurfaceGroup.Items.Count);
        Assert.Equal(0, optic.SurfaceGroup.Items[compiled.OriginalBackIndex].Thickness);
        Assert.Equal(0, optic.SurfaceGroup.Items[compiled.NullFrontIndex].Thickness);
        Assert.Equal(optic.SurfaceGroup.Items[compiled.NullFrontIndex].CoordinateSystem.Origin.Z, optic.SurfaceGroup.Items[compiled.NullBackIndex].CoordinateSystem.Origin.Z);
        foreach (var wave in source.Topology.Wavelengths)
        {
            var original = source.CreateOptic().Paraxial.GetTransferMatrix(1, source.SurfaceIndices["image"], wave.Nanometers);
            var actual = optic.Paraxial.GetTransferMatrix(compiled.ReferenceIndex, compiled.ImageIndex, wave.Nanometers);
            Assert.Equal(original.A, actual.A, 12); Assert.Equal(original.B, actual.B, 12);
            Assert.Equal(original.C, actual.C, 12); Assert.Equal(original.D, actual.D, 12);
        }
        Assert.Equal(0, measured.RayCount);
        Assert.False(TopologyCompiler.TryCompile(source.Topology with
        {
            Elements = source.Topology.Elements.SetItem(0, source.Topology.Elements[0] with { ThicknessMillimeters = 0, Transition = true })
        }, new() { AllowTransitionElements = true }).Succeeded);
        Assert.Throws<TopologyValidationException>(() => TopologyCompiler.Import(optic.ToSnapshot()));
    }

    [Fact]
    public void NullPairIndependentCurvatureDerivativesAreNonzeroAndMatchCoreFiniteDifferences()
    {
        var compiled = SpcTransitionCompiler.Compile(SpcTransitionCompiler.Create(Parent(), "crown", "null-derivative"));
        var optic = compiled.CreateOptic();
        OpticalDesignParameter[] parameters = [new(compiled.NullFrontIndex, OpticalDesignParameterKind.Curvature), new(compiled.NullBackIndex, OpticalDesignParameterKind.Curvature)];
        var initial = OpticalDesignParameters.Read(optic, parameters);
        var derivative = DifferentiableParaxialMetrics.TransferMatrix(optic, parameters, compiled.ReferenceIndex, compiled.ImageIndex);
        for (var column = 0; column < 2; column++)
        {
            const double h = 1e-6;
            var plus = initial.ToArray(); var minus = initial.ToArray(); plus[column] += h; minus[column] -= h;
            var p = OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, plus).Paraxial.GetTransferMatrix(compiled.ReferenceIndex, compiled.ImageIndex);
            var m = OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, minus).Paraxial.GetTransferMatrix(compiled.ReferenceIndex, compiled.ImageIndex);
            Assert.InRange(Math.Abs(derivative.A.Derivative(column) - (p.A - m.A) / (2 * h)), 0, 2e-8);
            Assert.InRange(Math.Abs(derivative.C.Derivative(column) - (p.C - m.C) / (2 * h)), 0, 2e-9);
            Assert.True(Math.Abs(derivative.C.Derivative(column)) > .01);
        }
        Assert.InRange(Math.Abs(derivative.C.Derivative(0) + derivative.C.Derivative(1)), 0, 2e-14);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    public void MaterializationCreatesRealGlassThenUsesTheOriginalFullAcceptance(double airGap)
    {
        var parent = Parent();
        var state = SpcTransitionCompiler.Create(parent, "crown", "added");
        var materialized = SpcTransitionCompiler.Materialize(state, 1, airGap, "N-F2");
        Assert.Equal(2, materialized.Topology.Elements.Length);
        Assert.Equal(airGap == 0, materialized.Topology.CementedInterfaces is not null);
        Assert.False(materialized.Topology.HasTransitionElements);
        Assert.Equal(1, materialized.Topology.Elements[1].ThicknessMillimeters);
        Assert.Equal(parent.Topology.Elements[0], materialized.Topology.Elements[0]);
        Assert.Equal(parent.Topology.Stop, materialized.Topology.Stop);
        Assert.Equal(parent.Topology.Image, materialized.Topology.Image);
        var targets = new TopologyTargets();
        var assessment = TopologyValidator.Validate(materialized, targets);
        Assert.True(assessment.Completed, assessment.Failure);
        Assert.Equal(targets, assessment.Targets);
        Assert.Equal(parent.Constraints, assessment.StructuralConstraints);
        Assert.Equal(TopologyValidator.GetProtocolFingerprint(materialized, targets), assessment.ProtocolFingerprint);
        Assert.DoesNotContain(assessment.Violations, violation => violation.Code == "transition-element");
        Assert.Equal(21, assessment.Fields.Length);
        Assert.True(assessment.TracedRayCount > 0);
    }

    [Fact]
    public void PerturbedNullFacesNeedRealAirAndCannotBeSilentlyMergedIntoAnUnequalSharedFace()
    {
        var state = SpcTransitionCompiler.Create(Parent(), "crown", "perturbed") with { NullCurvatureOffsetPerMillimeter = .001 };
        Assert.Equal(TopologyFailureCode.IdentityMismatch, Assert.Throws<TopologyValidationException>(() => SpcTransitionCompiler.Materialize(state, 1, 0)).Code);
        Assert.Null(SpcTransitionCompiler.Materialize(state, 1, .5).Topology.CementedInterfaces);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("limits")]
    [InlineData("version")]
    [InlineData("protected")]
    [InlineData("slots")]
    [InlineData("identifier")]
    public void ForgedOrUnsupportedTransitionsFailBeforeAnyOpticalTrace(string defect)
    {
        var parent = Parent();
        var state = SpcTransitionCompiler.Create(parent, "crown", "new-null");
        if (defect == "parent") state = state with { Baseline = state.Baseline with { Name = "changed" } };
        if (defect == "limits") state = state with { Constraints = state.Constraints with { MinimumCenterThicknessMillimeters = .2 } };
        if (defect == "version") state = state with { Version = 2 };
        if (defect == "identifier") state = state with { NewElementId = "front" };
        if (defect is "protected" or "slots")
        {
            var model = defect == "protected" ? parent.Topology with { Elements = [parent.Topology.Elements[0] with { Protected = true }] } : parent.Topology;
            var constraints = defect == "slots" ? parent.Constraints with { MaximumElementCount = 1 } : parent.Constraints;
            var compiled = TopologyCompiler.Compile(model, constraints);
            state = state with { Baseline = model, Constraints = constraints, BaselineBindingFingerprint = compiled.BindingFingerprint };
        }
        using var rays = SequentialTraceMeasurement.Begin();
        Assert.Throws<TopologyValidationException>(() => SpcTransitionCompiler.Compile(state));
        Assert.Equal(0, rays.RayCount);
    }

    [Theory]
    [InlineData(.05, 0)]
    [InlineData(.2, .01)]
    [InlineData(100, 0)]
    public void MaterializationCannotRelaxThicknessGapBackFocusOrTrackBounds(double thickness, double gap)
    {
        var state = SpcTransitionCompiler.Create(Parent(), "crown", "too-big");
        Assert.Throws<TopologyValidationException>(() => SpcTransitionCompiler.Materialize(state, thickness, gap));
    }

    [Fact]
    public async Task ExperimentalStateRoundTripsWithChecksumAndCannotBeLoadedAsAnAutonomousCheckpoint()
    {
        var state = SpcTransitionCompiler.Create(Parent(), "crown", "persisted");
        var directory = Path.Combine(Path.GetTempPath(), "OpticalSystemDesign-Spc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "experimental.spc-transition.json");
        try
        {
            await SpcTransitionFiles.SaveAsync(state, file);
            var loaded = await SpcTransitionFiles.LoadAsync(file);
            Assert.Equal(ContentFingerprint.Compute(state), ContentFingerprint.Compute(loaded));
            Assert.Equal(SpcTransitionCompiler.Compile(state).Fingerprint, SpcTransitionCompiler.Compile(loaded).Fingerprint);
            await Assert.ThrowsAsync<InvalidDataException>(() => new AutonomousSearchCheckpointStore().LoadAsync(file));
            var text = await File.ReadAllTextAsync(file);
            await File.WriteAllTextAsync(file, text.Replace("persisted", "tampered", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => SpcTransitionFiles.LoadAsync(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static CompiledTopology Parent()
    {
        var model = CementedTopologyTests.Example();
        return TopologyCompiler.Compile(model with { Elements = [model.Elements[0]], CementedInterfaces = null });
    }
}
