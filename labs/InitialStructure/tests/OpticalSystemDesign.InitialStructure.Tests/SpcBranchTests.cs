using System.Collections.Immutable;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Optimization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using OpticalSystemDesign.InitialStructure.Persistence;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class SpcBranchTests
{
    [Fact]
    public void GenericBfgsFindsMinimumButNeverCertifiesABoxBoundary()
    {
        static StationaryValue Quadratic(double[] x) => new(2 * Math.Pow(x[0] - .25, 2) + Math.Pow(x[1] + .5, 2), [4 * (x[0] - .25), 2 * (x[1] + .5)]);
        var minimum = StationaryPointSearch.Minimize(Quadratic, [1, 1], [-2, -2], [2, 2], 100, 1e-8);
        Assert.True(minimum.InteriorStationary);
        Assert.InRange(minimum.Evaluation.Value, 0, 1e-15);
        var boundary = StationaryPointSearch.Minimize(x => new(-x[0], [-1]), [0], [-1], [1], 20, 1e-8);
        Assert.False(boundary.InteriorStationary); Assert.Equal(1, boundary.Coordinates[0]);
    }

    [Fact]
    public void TrueGradientCurvatureResolvesAnIndefiniteHessianAndTwoBasins()
    {
        static StationaryValue Merit(double[] x) => new(Math.Pow(x[0] * x[0] - 1, 2) + x[1] * x[1], [4 * x[0] * (x[0] * x[0] - 1), 2 * x[1]]);
        var spectrum = StationaryPointSearch.Curvature(Merit, [0, 0], [-2, -2], [2, 2], 1e-4);
        Assert.InRange(spectrum.Eigenvalues[0], -4, -3.999999);
        Assert.Equal(2, spectrum.Eigenvalues[1], 10);
        for (var i = 0; i < 2; i++) for (var j = 0; j < 2; j++)
            Assert.InRange(Math.Abs(spectrum.Hessian[i][j] - Enumerable.Range(0, 2).Sum(k => spectrum.Eigenvalues[k] * spectrum.Eigenvectors[k][i] * spectrum.Eigenvectors[k][j])), 0, 1e-12);
        foreach (var sign in new[] { -1, 1 })
        {
            var minimum = StationaryPointSearch.Minimize(Merit, [sign * .05, .1], [-2, -2], [2, 2], 100, 1e-8);
            Assert.True(minimum.InteriorStationary); Assert.InRange(Math.Abs(minimum.Coordinates[0] - sign), 0, 1e-7);
            Assert.All(StationaryPointSearch.Curvature(Merit, minimum.Coordinates, [-2, -2], [2, 2], 1e-4).Eigenvalues, e => Assert.True(e > 0));
        }
    }

    [Fact]
    public void GenericSolverRejectsInvalidGradientsAndCancelsBeforeEvaluation()
    {
        Assert.Throws<ArithmeticException>(() => StationaryPointSearch.Curvature(_ => new(0, []), [0], [-1], [1], .001));
        Assert.Throws<ArithmeticException>(() => StationaryPointSearch.Minimize(_ => new(double.NaN, [0]), [0], [-1], [1], 1, 1e-7));
        Assert.Throws<ArgumentException>(() => StationaryPointSearch.Minimize(_ => new(0, [0]), [2], [-1], [1], 1, 1e-7));
        Assert.Throws<OperationCanceledException>(() => StationaryPointSearch.Minimize(_ => throw new Exception("Must not evaluate"), [0], [-1], [1], 1, 1e-7, new(true)));
    }

    [Theory]
    [InlineData(-.0001)]
    [InlineData(.0001)]
    public void SignedCoreContinuationMatchesFiniteDifferencesWithoutChangingOrdinaryTrace(double perturbation)
    {
        var compiled = SpcTransitionCompiler.Compile(SpcTransitionCompiler.Create(SpcTransitionTests.Parent(), "crown", "signed-null"));
        var optic = compiled.CreateOptic();
        OpticalDesignParameter[] parameters = [new(compiled.NullFrontIndex, OpticalDesignParameterKind.Curvature, .01), new(compiled.NullBackIndex, OpticalDesignParameterKind.Curvature, .01)];
        var point = OpticalDesignParameters.Read(optic, parameters); point[0] += perturbation / .01; point[1] -= perturbation / .01;
        var pupils = ApertureSampler.GenerateGaussianQuadrature(2, 8).ToArray();
        var changed = OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, point);
        Assert.Contains("formal ray", Assert.ThrowsAny<ArithmeticException>(() => DifferentiableOpticalMetrics.EvaluateAtFields(changed, parameters, [(0, 1)], pupils, computeMode: DesignComputeMode.Cpu, usePhysicalStop: true)).Message);
        var at = Evaluate(point);
        Assert.Equal("cpu-null-element-continuation/1", at.Execution!.Backend);
        var value = at.Fields[0].RmsRadiusMillimeters;
        for (var j = 0; j < 2; j++)
        {
            const double h = 1e-5; var plus = point.ToArray(); var minus = point.ToArray(); plus[j] += h; minus[j] -= h;
            var fd = (Evaluate(plus).Fields[0].RmsRadiusMillimeters.Value - Evaluate(minus).Fields[0].RmsRadiusMillimeters.Value) / (2 * h);
            Assert.InRange(Math.Abs(value.Derivative(j) - fd), 0, 2e-7);
        }
        Assert.Contains("formal ray", Assert.ThrowsAny<ArithmeticException>(() => DifferentiableOpticalMetrics.EvaluateAtFields(changed, parameters, [(0, 1)], pupils, computeMode: DesignComputeMode.Cpu, usePhysicalStop: true)).Message);
        Assert.Throws<TopologyValidationException>(() => TopologyCompiler.Import(changed.ToSnapshot()));
        DifferentiableOpticalResult Evaluate(double[] x) => DifferentiableOpticalMetrics.EvaluateNullElementContinuation(
            OpticalDesignParameters.Apply(optic.ToSnapshot(), parameters, x), parameters, [(0, 1)], pupils, compiled.NullFrontIndex, compiled.NullBackIndex);
    }

    [Theory]
    [InlineData("range")]
    [InlineData("positive-thickness")]
    [InlineData("media")]
    public void ContinuationRejectsForgedPhysicalStatesBeforeTrace(string defect)
    {
        var compiled = SpcTransitionCompiler.Compile(SpcTransitionCompiler.Create(SpcTransitionTests.Parent(), "crown", "null"));
        var snapshot = compiled.CreateOptic().ToSnapshot();
        if (defect == "positive-thickness") snapshot.Surfaces[compiled.NullFrontIndex] = snapshot.Surfaces[compiled.NullFrontIndex] with { Thickness = .1 };
        if (defect == "media") snapshot.Surfaces[compiled.NullFrontIndex] = snapshot.Surfaces[compiled.NullFrontIndex] with { Material = "N-F2", Components = null };
        var optic = OpticalSystemDesign.Core.Optic.FromSnapshot(snapshot);
        using var rays = SequentialTraceMeasurement.Begin();
        Assert.ThrowsAny<ArgumentException>(() => DifferentiableOpticalMetrics.EvaluateNullElementContinuation(optic, [], [(0, 0)],
            ApertureSampler.GenerateGaussianQuadrature(2, 8).ToArray(), defect == "range" ? 1 : compiled.NullFrontIndex, compiled.NullBackIndex));
        Assert.Equal(0, rays.RayCount);
    }

    [Theory]
    [InlineData("aperture", "OriginalExternalStopApertureConflict")]
    [InlineData("permission", "OriginalInsertionPermissionConflict")]
    [InlineData("slots", "OriginalElementLimit")]
    [InlineData("protected", "NoPermittedSphericalPostStopInterface")]
    public void OriginalRestrictionsStopSpcBeforeAnyPhysicalWork(string defect, string expected)
    {
        var plan = Plan();
        if (defect == "aperture") plan = plan with { Seeds = [plan.Seeds[0] with { Stop = plan.Seeds[0].Stop with { ZMillimeters = 0, ClearSemiDiameterMillimeters = 6.25 } }] };
        if (defect == "permission") plan = plan with { Options = plan.Options with { Expansion = new() { EnableInsertion = false } } };
        if (defect == "slots") plan = plan with { Constraints = plan.Constraints with { MaximumElementCount = 1 } };
        if (defect == "protected") plan = plan with { Seeds = [plan.Seeds[0] with { Elements = [plan.Seeds[0].Elements[0] with { Protected = true }] }] };
        using var rays = SequentialTraceMeasurement.Begin();
        var result = SpcBranchSearch.Run(plan, Budget(), new());
        Assert.Equal(expected, result.Parents.IsEmpty ? result.StopReason : result.Parents[0].Outcome);
        Assert.Equal(0, rays.RayCount); Assert.Equal(0, result.Work.FunctionEvaluations); Assert.Empty(result.Branches);
    }

    [Fact]
    public void CancellationAndActualRayBudgetCannotPromoteAPartialBranch()
    {
        var cancelled = SpcBranchSearch.Run(Plan(), Budget(), new(), new(true));
        Assert.Equal("Cancelled", cancelled.StopReason); Assert.Equal(0, cancelled.Work.TracedRays);
        var limited = SpcBranchSearch.Run(Plan(), Budget() with { MaximumTracedRays = 100 }, new());
        Assert.InRange(limited.Work.TracedRays, 1, 100); Assert.Empty(limited.QualifiedBranchIds);
        Assert.NotEqual("Completed", limited.StopReason);
    }

    [Fact]
    public async Task GenuineSpcCertifiesSaddleAndTwoMinimaBeforeOriginalPhysicalAcceptanceAndPortableReadback()
    {
        var plan = Plan(); var policy = new SpcBranchOptions(); var budget = Budget();
        var before = ContentFingerprint.Compute(plan);
        var result = SpcBranchSearch.Run(plan, budget, policy);
        Assert.Equal("Completed", result.StopReason);
        var parent = Assert.Single(result.Parents);
        Assert.True(parent.ParentStationarity!.Certified); Assert.True(parent.Saddle!.Certified);
        Assert.Equal(1, parent.Saddle.StableNegativeDirections); Assert.True(parent.Saddle.Fine.Eigenvalues[0] < -parent.Saddle.CurvatureUncertainty);
        Assert.Equal(2, result.QualifiedBranchIds.Length);
        Assert.Equal(new[] { -1, 1 }, result.Branches.Select(b => b.Sign));
        foreach (var branch in result.Branches)
        {
            Assert.True(branch.OptimizedObjective < branch.InitialObjective); Assert.True(branch.NullMinimum!.Certified);
            Assert.True(branch.NullInteriorStationary); Assert.Equal(0, branch.NullMinimum.StableNegativeDirections);
            Assert.Equal("QualifiedOriginalSpecification", branch.Outcome);
            Assert.True(branch.Assessment!.MeetsTargets); Assert.Equal(21, branch.Assessment.Fields.Length);
            Assert.Equal(plan.Targets, branch.Assessment.Targets); Assert.Equal(plan.Constraints, branch.Assessment.StructuralConstraints);
            Assert.False(branch.Endpoint!.HasTransitionElements); Assert.Null(branch.Endpoint.CementedInterfaces);
            Assert.Equal(plan.Seeds[0].Stop, branch.Endpoint.Stop); Assert.Equal(plan.Seeds[0].Image, branch.Endpoint.Image);
            Assert.Equal(2, branch.Endpoint.Elements.Length);
            foreach (var stage in branch.Stages)
            {
                Assert.Equal(stage.Topology!.Elements[1].ThicknessMillimeters, stage.ThicknessMillimeters);
                Assert.Equal(5, stage.Topology.Elements[1].Front.ClearSemiDiameterMillimeters);
                Assert.Equal(5, stage.Topology.Elements[1].Back.ClearSemiDiameterMillimeters);
                Assert.True(stage.ThicknessMillimeters >= plan.Constraints.MinimumCenterThicknessMillimeters);
                Assert.Contains(stage.Material, plan.Options.InsertionMaterials);
            }
        }
        Assert.Equal(before, ContentFingerprint.Compute(plan));
        Assert.InRange(result.Work.FunctionEvaluations, 1, budget.MaximumFunctionEvaluations);
        Assert.InRange(result.Work.GradientEvaluations, 1, policy.MaximumGradientEvaluations);
        Assert.InRange(result.Work.TracedRays, 1, budget.MaximumTracedRays);
        var directory = Path.Combine(Path.GetTempPath(), "spc-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "study.json"); var study = new SpcBranchStudy(plan, budget, policy, result);
            await SpcBranchStudyFiles.SaveAsync(study, path);
            using var measured = SequentialTraceMeasurement.Begin();
            var reopened = await SpcBranchStudyFiles.LoadAsync(path);
            Assert.Equal(ContentFingerprint.Compute(study), ContentFingerprint.Compute(reopened)); Assert.Equal(0, measured.RayCount);
            await Assert.ThrowsAsync<InvalidDataException>(() => new AutonomousSearchCheckpointStore().LoadAsync(path));
            await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("parent-0:minus", "forged:minus", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => SpcBranchStudyFiles.LoadAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static AutonomousSearchPlan Plan() => new()
    {
        Seeds = [SpcTransitionTests.Parent().Topology with { Name = "SPC existing authored singlet" }],
        Options = new() { Seed = 42, InsertionMaterials = ["N-BK7", "N-F2"], Expansion = new() { EnableInsertion = true, EnableMaterial = true },
            Local = new() { ComputeMode = DesignComputeMode.Cpu, PerformFinalAssessment = false, GateCompensationVersion = 4,
                Sampling = new() { FieldsPerDirection = 3, PupilRings = 2, PupilArms = 8, IncludeBoundaryClearance = true, IncludeBoundaryMaximum = true } } }
    };
    internal static ParaxialSeedSearchOptions Budget() => new() { MaximumFunctionEvaluations = 600, MaximumTracedRays = 2_000_000, TimeLimit = TimeSpan.FromSeconds(120) };
}
