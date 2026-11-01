using System.Collections.Immutable;
using System.Text.Json;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class GateCompensationTests
{
    private static readonly string[] Ids = ["target:power", "target:inverse-f-number", "geometry:edge", "field:0:rms",
        "field:0:maximum", "field:0:distortion", "field:0:ray:0:clearance", "field:0:ray:0:x", "field:0:ray:0:y",
        "field:0:boundary:0:clearance"];

    private static TopologyObjectiveEvaluation Evaluation(double power = 0, double geometry = -1,
        double rms = 1, double clearance = 1, double secondClearance = -1, double image = 1) => new()
    {
        LayoutFingerprint = "same-layout", OpticFingerprint = "test", Available = true,
        Residuals = [power, 0, Math.Max(0, geometry), Math.Max(0, rms), Math.Max(0, rms), 0,
            Math.Max(0, clearance), image, 0, Math.Max(0, secondClearance)],
        Gradient = [1], Jacobian = Enumerable.Range(0, Ids.Length).Select(_ => ImmutableArray.Create(1.0)).ToImmutableArray(),
        ConstraintTangents = new[] { (0, power), (1, 0.0), (2, geometry), (3, rms), (4, rms), (5, -1.0),
            (6, clearance), (9, secondClearance) }.Select(v => new TopologyConstraintTangent(v.Item1, v.Item2, [1.0])).ToImmutableArray()
    };

    [Fact]
    public void ClearanceRecoveryCannotTradeAwayFocusOrManufacturing()
    {
        var gates = new TopologyGateCompensation(Ids); var before = Evaluation();
        Assert.True(gates.Accept(before, Evaluation(clearance: .5, rms: 2, image: 2)));
        Assert.False(gates.Accept(before, Evaluation(power: 1.1, clearance: .1)));
        Assert.False(gates.Accept(before, Evaluation(geometry: .01, clearance: .1)));
        Assert.True(gates.Accept(before, Evaluation(clearance: .1, secondClearance: .01)));
    }

    [Fact]
    public void ImageCompensationCannotReintroduceClippingAndMustReduceTheJointImageMerit()
    {
        var gates = new TopologyGateCompensation(Ids); var before = Evaluation(clearance: -1);
        Assert.True(gates.Accept(before, Evaluation(clearance: -1, rms: .5, image: .5)));
        Assert.False(gates.Accept(before, Evaluation(clearance: .1, rms: .1, image: .1)));
        Assert.False(gates.Accept(before, Evaluation(clearance: -1, rms: 2, image: 2)));
    }

    [Fact]
    public void FailedClearanceRowsCanTradeProgressInsteadOfRequiringEveryFailedRowToDecrease()
    {
        var gates = new TopologyGateCompensation(Ids);
        Assert.True(gates.Accept(Evaluation(clearance: 2, secondClearance: 1), Evaluation(clearance: .5, secondClearance: 1.1)));
        Assert.False(gates.Accept(Evaluation(clearance: 1, secondClearance: 1), Evaluation(clearance: 2, secondClearance: .5)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1e-6, false)]
    public void CompleteTrainingGatesAreRequiredForConvergence(double clearance, bool expected)
    {
        var gates = new TopologyGateCompensation(Ids);
        Assert.Equal(expected, gates.Converged(Evaluation(rms: -.1, clearance: clearance, image: .01)));
    }

    [Fact]
    public void FixedApertureConflictHasExplicitExternalStopAndAxialScope()
    {
        var (plan, _) = StructureEvolutionTests.Specification();
        var topology = plan.Seeds[0] with { Stop = new("stop", plan.Seeds[0].Reference.ZMillimeters, 6.25) };
        topology = topology with { Elements = topology.Elements.Select(e => e with
        {
            Front = e.Front with { ClearSemiDiameterMillimeters = 5 }, Back = e.Back with { ClearSemiDiameterMillimeters = 5 }
        }).ToImmutableArray() };
        var conflict = StructureSearchDiagnostics.ExternalStopAperturePrecheck(topology);
        Assert.True(conflict.ProvenInfeasible); Assert.Equal(6.25, conflict.Actual); Assert.Equal(5, conflict.Limit);
        Assert.False(StructureSearchDiagnostics.ExternalStopAperturePrecheck(topology with { Stop = topology.Stop with { ClearSemiDiameterMillimeters = 5 } }).ProvenInfeasible);
        Assert.False(StructureSearchDiagnostics.ExternalStopAperturePrecheck(topology with { Stop = topology.Stop with { ClearSemiDiameterMillimeters = 5 + 1e-13 } }).ProvenInfeasible);
        Assert.False(StructureSearchDiagnostics.ExternalStopAperturePrecheck(topology with { Stop = topology.Stop with { ZMillimeters = topology.Elements[0].BackZMillimeters + .1 } }).ProvenInfeasible);
        Assert.False(StructureSearchDiagnostics.ExternalStopAperturePrecheck(topology with { Fields = [new("off-axis", 0, 5)] }).ProvenInfeasible);
    }

    [Fact]
    public void NullPolicyDoesNotAddFieldsToHistoricalJsonAndUnknownPolicyIsRejected()
    {
        Assert.DoesNotContain("GateCompensationVersion", JsonSerializer.Serialize(new FixedTopologyOptions()));
        Assert.DoesNotContain("GateCompensationVersion", JsonSerializer.Serialize(new StructureEvolutionOptions()));
        var (plan, options) = StructureEvolutionTests.Specification();
        Assert.Throws<ArgumentException>(() => ParaxialSeedSearch.Run(plan,
            options with { Evolution = options.Evolution! with { GateCompensationVersion = 2 } }));
    }

    [Fact]
    public void RealCoreCompensationResumesWithTheSameGatesAndAnIndependentFinalAssessment()
    {
        var (plan, _) = StructureEvolutionTests.Specification();
        var source = TopologyCompiler.Compile(plan.Seeds[0], plan.Constraints);
        var fingerprint = source.OpticFingerprint;
        var binding = TopologyParameterBinder.Bind(source, StructureEvolutionSearch.EvolutionVariables(source.Topology, plan.Options));
        var targets = plan.Targets with { MaximumRmsRadiusMillimeters = .001, MaximumSpotRadiusMillimeters = .003 };
        var options = new FixedTopologyOptions
        {
            GateCompensationVersion = 1, MaximumUpdates = 3, MaximumFunctionEvaluations = 5, MaximumGradientEvaluations = 4,
            ComputeMode = DesignComputeMode.Cpu,
            Sampling = new() { FieldsPerDirection = 3, PupilRings = 2, PupilArms = 8, ProfileRadialSamples = 64,
                IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true }
        };
        using var stop = new CancellationTokenSource();
        var first = FixedTopologyOptimizer.Run(source, binding, targets, options, stop.Token, _ => stop.Cancel());
        Assert.Equal(FixedTopologyTermination.Cancelled, first.Termination);
        Assert.Single(first.Steps); Assert.Null(first.FinalAssessment); Assert.NotNull(first.Continuation);
        var continued = FixedTopologyOptimizer.Run(source, binding, targets, options, continuation: first.Continuation);
        Assert.Equal("fixed-topology-gate-compensation-v1", continued.Algorithm);
        Assert.True(continued.Work.TracedRays > first.Work.TracedRays);
        Assert.InRange(continued.Work.FunctionEvaluations, first.Work.FunctionEvaluations + 1, options.MaximumFunctionEvaluations);
        Assert.InRange(continued.Work.GradientEvaluations, first.Work.GradientEvaluations, options.MaximumGradientEvaluations);
        Assert.Equal(first.Steps[0], continued.Steps[0]);
        Assert.Equal(fingerprint, source.OpticFingerprint); Assert.Equal(source.Topology.Stop, continued.Topology.Stop);
        Assert.Equal(source.Topology.Image, continued.Topology.Image);
        Assert.Equal(source.Topology.Elements.Select(e => (e.Front.ClearSemiDiameterMillimeters, e.Back.ClearSemiDiameterMillimeters)),
            continued.Topology.Elements.Select(e => (e.Front.ClearSemiDiameterMillimeters, e.Back.ClearSemiDiameterMillimeters)));
        var independent = TopologyValidator.Validate(TopologyCompiler.Compile(continued.Topology, source.Constraints), targets);
        Assert.True(continued.FinalAssessment!.Completed);
        Assert.Equal(independent.ProtocolFingerprint, continued.FinalAssessment.ProtocolFingerprint);
        Assert.Equal(independent.OpticFingerprint, continued.FinalAssessment.OpticFingerprint);
        Assert.Equal(independent.MeetsTargets, continued.FinalAssessment.MeetsTargets);
        Assert.Equal(independent.Violations.Length, continued.FinalAssessment.Violations.Length);
        foreach (var (expected, actual) in independent.Violations.Zip(continued.FinalAssessment.Violations))
        {
            Assert.Equal(expected.Code, actual.Code); Assert.Equal(expected.Limit, actual.Limit);
            if (expected.Actual is { } number) Assert.Equal(number, actual.Actual!.Value, 11);
            else Assert.Null(actual.Actual);
        }
        Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(source, binding, targets,
            options with { GateCompensationVersion = null }, continuation: first.Continuation));
    }

    [Fact]
    public void DisabledOperationsDoNotConsumeTheReductionPositionBudget()
    {
        var (plan, options) = StructureEvolutionTests.Specification();
        plan = plan with { Options = plan.Options with { Expansion = plan.Options.Expansion! with { EnableInsertion = false } } };
        options = options with { Evolution = options.Evolution! with
        {
            GateCompensationVersion = 1, Operations = [TopologyOperationKind.Delete, TopologyOperationKind.Insert],
            MaximumBranches = 4, OptimizeParents = false, CompensationUpdatesPerBranch = 0,
            Mode = StructureEvolutionMode.DirectCompensation
        } };
        var result = ParaxialSeedSearch.Run(plan, options);
        Assert.Equal(4, result.Evolution!.Trials.Length);
        Assert.All(result.Evolution.Trials, trial => Assert.Equal("delete", trial.Operation));
        var changes = result.Evolution.Trials.Select(t => t.Change).Where(c => c is not null).ToArray();
        Assert.Equal(3, changes.DistinctBy(c => JsonSerializer.Serialize(c)).Count());
        var duplicate = Assert.Single(result.Evolution.Trials.Where(t => t.Outcome == "DuplicateEditSkipped"));
        Assert.Equal(0, duplicate.Work.FunctionEvaluations); Assert.Equal(0, duplicate.Work.TracedRays);
    }

    [Fact]
    public void OriginalTaskPrecheckStopsWithoutChangingApertureOrClaimingQualification()
    {
        var (plan, options) = StructureEvolutionTests.Specification();
        plan = plan with { Seeds = [plan.Seeds[0] with { Stop = new("stop", plan.Seeds[0].Reference.ZMillimeters, 100) }] };
        var fingerprint = ContentFingerprint.Compute(plan);
        var result = ParaxialSeedSearch.Run(plan, options with { Evolution = options.Evolution! with { GateCompensationVersion = 1 } });
        Assert.Equal(fingerprint, ContentFingerprint.Compute(plan));
        Assert.Equal("ProvenFrontStopApertureConflict", result.Evolution!.StopReason);
        Assert.Empty(result.Evolution.QualifiedArchive); Assert.Empty(result.Seeds);
        Assert.All(result.Evolution.Prechecks!.Value, check => Assert.True(check.ProvenInfeasible));
        Assert.Equal(0, result.Work.TracedRays); Assert.Equal(0, result.Work.FunctionEvaluations);
    }
}
