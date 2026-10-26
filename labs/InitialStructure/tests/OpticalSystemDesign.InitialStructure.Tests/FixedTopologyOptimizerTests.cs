using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;
using Xunit.Abstractions;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class FixedTopologyOptimizerTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static readonly TopologyTrainingSampling Small = new() { PupilRings = 1, PupilArms = 8, ProfileRadialSamples = 8 };

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void SmallBudgetOptimizesRealArbitraryTopologyAndIndependentlyValidatesItsActualEndpoint(int count)
    {
        var source = Source(count);
        var parent = source.TopologyFingerprint;
        var initialPhysical = TopologyValidator.Validate(source, new());
        Assert.True(initialPhysical.Completed, initialPhysical.Failure);
        var binding = Bind(source);
        var options = new FixedTopologyOptions
        {
            MaximumUpdates = 20,
            MaximumFunctionEvaluations = 22,
            MaximumGradientEvaluations = 21,
            ComputeMode = DesignComputeMode.Cpu,
            LearningRate = .02,
            Sampling = Small
        };
        var result = FixedTopologyOptimizer.Run(source, binding, new(), options);
        Assert.NotNull(result.InitialEvaluation);
        Assert.True(result.InitialEvaluation!.Available, result.InitialEvaluation.Failure);
        Assert.True(result.AcceptedUpdates > 0, string.Join("; ", result.Diagnostics));
        Assert.True(result.EndpointEvaluation!.Available);
        Assert.True(result.EndpointEvaluation.Objective < result.InitialEvaluation.Objective,
            $"{result.InitialEvaluation.Objective} -> {result.EndpointEvaluation.Objective}");
        Assert.Equal(parent, source.TopologyFingerprint);
        Assert.Equal(count, result.Topology.Elements.Length);
        Assert.Equal(FixedTopologyTermination.UpdateLimit, result.Termination);
        Assert.Equal(22, result.Work.FunctionEvaluations);
        Assert.Equal(21, result.Work.GradientEvaluations);
        Assert.Equal(21, result.Work.CompletedGradientEvaluations);
        Assert.Equal(1, result.Work.ValidationEvaluations);
        Assert.True(result.Work.TracedRays > 0);
        var final = TopologyCompiler.Compile(result.Topology, source.Constraints);
        Assert.Equal(final.OpticFingerprint, result.FinalAssessment!.OpticFingerprint);
        Assert.True(result.FinalAssessment.Completed, result.FinalAssessment.Failure);
        Assert.Equal(21, result.FinalAssessment.Fields.Length);
        Assert.All(result.FinalAssessment.Fields, field => Assert.Equal(3 * 320, field.AttemptedRays));
        AssertSameFormalAssessment(final, result.FinalAssessment);
        output.WriteLine($"{count} elements: objective {result.InitialEvaluation.Objective:R} -> {result.EndpointEvaluation.Objective:R}; accepted={result.AcceptedUpdates}; RMS={result.FinalAssessment.Fields.Max(field => field.RmsRadiusMillimeters):R}; meets={result.FinalAssessment.MeetsTargets}; {result.Work.TracedRays} rays");
        Evidence("optimization-" + count, new
        {
            Source = source.Topology,
            Options = options,
            InitialPhysical = initialPhysical,
            Result = result,
            FinalSnapshot = final.Snapshot,
            BenchmarkTotalTracedRays = initialPhysical.TracedRayCount + result.Work.TracedRays
        });
    }

    [Fact]
    public void EverySelectedParameterAndFixedResidualJacobianMatchesCentralDifferences()
    {
        using var measurement = SequentialTraceMeasurement.Begin();
        var source = Source(3);
        var binding = Bind(source, allShapes: true);
        var targets = new TopologyTargets { MaximumDistortionFraction = .0005, MinimumEdgeThicknessMillimeters = .4 };
        var objective = new FixedTopologyObjective(source, binding, targets, Small);
        var nominal = objective.Evaluate(binding.InitialCoordinates, includeJacobian: true);
        Assert.True(nominal.Available, nominal.Failure);
        Assert.Equal(objective.ResidualIds.Length, nominal.Residuals.Length);
        Assert.Equal(binding.Slots.Length, nominal.Gradient.Length);
        double maximumObjective = 0, maximumResidual = 0;
        const double h = 1e-4;
        for (var parameter = 0; parameter < binding.Slots.Length; parameter++)
        {
            var plus = binding.InitialCoordinates.ToArray(); var minus = binding.InitialCoordinates.ToArray();
            plus[parameter] += h; minus[parameter] -= h;
            var a = objective.Evaluate(plus); var b = objective.Evaluate(minus);
            Assert.True(a.Available, a.Failure); Assert.True(b.Available, b.Failure);
            var fd = (a.Objective!.Value - b.Objective!.Value) / (2 * h);
            var error = Math.Abs(fd - nominal.Gradient[parameter]) / Math.Max(1, Math.Max(Math.Abs(fd), Math.Abs(nominal.Gradient[parameter])));
            maximumObjective = Math.Max(maximumObjective, error);
            Assert.True(error <= 5e-5, $"parameter {parameter} {binding.Slots[parameter].Request}: objective {nominal.Objective:R}, AD {nominal.Gradient[parameter]:R}, FD {fd:R}, error {error:R}");
            for (var row = 0; row < nominal.Residuals.Length; row++)
            {
                var difference = (a.Residuals[row] - b.Residuals[row]) / (2 * h);
                var derivative = nominal.Jacobian[row][parameter];
                var normalized = Math.Abs(difference - derivative) / Math.Max(1, Math.Max(Math.Abs(difference), Math.Abs(derivative)));
                maximumResidual = Math.Max(maximumResidual, normalized);
                Assert.True(normalized <= 5e-5, $"{parameter} {objective.ResidualIds[row]}: AD {derivative:R}, FD {difference:R}, error {normalized:R}");
            }
        }
        output.WriteLine($"{binding.Slots.Length} parameters, {nominal.Residuals.Length} residuals; objective max={maximumObjective:R}, residual max={maximumResidual:R}");
        Evidence("all-parameter-gradients", new
        {
            Targets = targets,
            Parameters = binding.Slots.Select(slot => slot.Request),
            Residuals = nominal.Residuals.Length,
            ScalarJacobianEntries = nominal.Residuals.Length * binding.Slots.Length,
            MaximumObjectiveNormalizedError = maximumObjective,
            MaximumResidualNormalizedError = maximumResidual,
            DifferenceStep = h,
            EvaluationAndGradientRequests = 1 + 2 * binding.Slots.Length,
            TotalTracedRays = measurement.RayCount
        });
    }

    [Fact]
    public void ClippingCannotRemoveResidualSlotsOrObtainFalseAcceptance()
    {
        using var measurement = SequentialTraceMeasurement.Begin();
        var source = Source(3);
        var lens = source.Topology.Elements[0];
        var clippedModel = source.Topology with
        {
            Elements = source.Topology.Elements.SetItem(0,
            lens with { Front = lens.Front with { ClearSemiDiameterMillimeters = .1 } })
        };
        var clipped = TopologyCompiler.Compile(clippedModel);
        var binding = Bind(source); var clippedBinding = Bind(clipped);
        var a = new FixedTopologyObjective(source, binding, new(), Small);
        var b = new FixedTopologyObjective(clipped, clippedBinding, new(), Small);
        Assert.True(a.ResidualIds.SequenceEqual(b.ResidualIds));
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
        var result = b.Evaluate(clippedBinding.InitialCoordinates);
        Assert.True(result.Available, result.Failure);
        Assert.Equal(a.ResidualIds.Length, result.Residuals.Length);
        Assert.Contains(a.ResidualIds.Select((id, index) => (id, value: result.Residuals[index])), row => row.id.EndsWith(":clearance", StringComparison.Ordinal) && row.value > 0);
        for (var parameter = 0; parameter < clippedBinding.Slots.Length; parameter++)
        {
            const double h = 1e-5;
            var plus = clippedBinding.InitialCoordinates.ToArray(); var minus = clippedBinding.InitialCoordinates.ToArray();
            plus[parameter] += h; minus[parameter] -= h;
            var forward = b.Evaluate(plus); var backward = b.Evaluate(minus);
            Assert.True(forward.Available, forward.Failure); Assert.True(backward.Available, backward.Failure);
            var difference = (forward.Objective!.Value - backward.Objective!.Value) / (2 * h);
            Assert.InRange(Math.Abs(difference - result.Gradient[parameter]) / Math.Max(1, Math.Abs(difference)), 0, 5e-5);
        }
        var formal = TopologyValidator.Validate(clipped, new());
        Assert.True(formal.Completed, formal.Failure);
        Assert.False(formal.MeetsTargets);
        Assert.Contains(formal.Fields, field => field.ValidRays < field.AttemptedRays);
        Assert.Contains(formal.Violations, violation => violation.Code.EndsWith("lost-fraction", StringComparison.Ordinal));
        Evidence("clipping", new
        {
            Layout = b.ResidualIds,
            Evaluation = result,
            Physical = formal,
            DifferenceProbeRequests = 2 * clippedBinding.Slots.Length,
            TotalTracedRays = measurement.RayCount
        });
    }

    [Fact]
    public void SingularProfileAndMissingCoordinatesKeepAFailedFixedLayoutAndChargeTheirAttempts()
    {
        var source = Source(3);
        var binding = TopologyParameterBinder.Bind(source, [new("lens-1:front", OpticalDesignParameterKind.Curvature, 0, .3)]);
        var objective = new FixedTopologyObjective(source, binding, new(), Small);
        var invalid = objective.Evaluate([.2]);
        Assert.False(invalid.Available);
        Assert.Null(invalid.Objective);
        Assert.Empty(invalid.Gradient);
        Assert.Equal(objective.ResidualIds.Length, invalid.Residuals.Length);
        Assert.NotNull(invalid.Failure);
        var broken = source.Topology with
        {
            Elements = source.Topology.Elements.SetItem(0, source.Topology.Elements[0] with
            { Front = source.Topology.Elements[0].Front with { Profile = source.Topology.Elements[0].Front.Profile with { CurvaturePerMillimeter = .2 } } })
        };
        var compiled = TopologyCompiler.Compile(broken);
        var result = FixedTopologyOptimizer.Run(compiled, TopologyParameterBinder.Bind(compiled,
            [new("lens-1:front", OpticalDesignParameterKind.Curvature, 0, .3)]), new(), new() { MaximumUpdates = 2, MaximumFunctionEvaluations = 4, ComputeMode = DesignComputeMode.Cpu, Sampling = Small });
        Assert.Equal(FixedTopologyTermination.DomainFailure, result.Termination);
        Assert.Equal(1, result.Work.GradientEvaluations);
        Assert.Equal(0, result.Work.CompletedGradientEvaluations);
        Assert.Equal(2, result.Work.FunctionEvaluations);
        Assert.False(result.FinalAssessment!.MeetsTargets);
        Assert.Equal(broken, result.Topology);
        Evidence("domain-failure", result);
    }

    [Fact]
    public void CriticalRefractionIsAnExplicitChargedFailureInsteadOfAZeroGradient()
    {
        var index = new Core.Optic().Materials.Resolve("N-BK7").RefractiveIndex(587.6);
        var radial = double.Hypot(ApertureSampler.GenerateGaussianQuadrature(1, 4)[0].X,
            ApertureSampler.GenerateGaussianQuadrature(1, 4)[0].Y);
        var model = new DesignTopology
        {
            Name = "Critical refraction",
            Stop = new("stop", 0, 1),
            Image = new("image", 5),
            Fields = [new("axis", 0, 0)],
            Wavelengths = [new() { Label = "d", Nanometers = 587.6, IsPrimary = true, Weight = 1 }],
            Elements = [new() { Id = "critical", FrontSurfaceId = "critical:front", BackSurfaceId = "critical:back", FrontZMillimeters = 1,
                ThicknessMillimeters = 1, Material = "N-BK7", Front = new() { Profile = new() { Kind = TopologyProfileKind.Plane }, ClearSemiDiameterMillimeters = .8 },
                Back = new() { Profile = new() { CurvaturePerMillimeter = (1 - 1e-14) / (index * radial) }, ClearSemiDiameterMillimeters = .8 } }]
        };
        var source = TopologyCompiler.Compile(model);
        var binding = TopologyParameterBinder.Bind(source, [new("critical:back", OpticalDesignParameterKind.Curvature, 0, 2, .1)]);
        var sampling = Small with { PupilArms = 4 };
        var objective = new FixedTopologyObjective(source, binding, new(), sampling);
        var failed = objective.Evaluate(binding.InitialCoordinates);
        Assert.False(failed.Available); Assert.Empty(failed.Gradient); Assert.Null(failed.Objective);
        Assert.True(failed.TracedRayCount > 0);
        Assert.Contains("refraction", failed.Failure!, StringComparison.OrdinalIgnoreCase);
        var result = FixedTopologyOptimizer.Run(source, binding, new(), new()
        {
            MaximumUpdates = 0,
            MaximumFunctionEvaluations = 2,
            ComputeMode = DesignComputeMode.Cpu,
            Sampling = sampling
        });
        Assert.Equal(FixedTopologyTermination.DomainFailure, result.Termination);
        Assert.Equal(1, result.Work.GradientEvaluations); Assert.Equal(0, result.Work.CompletedGradientEvaluations);
        Assert.True(result.Work.TracedRays >= failed.TracedRayCount);
        Assert.False(result.FinalAssessment!.MeetsTargets);
        Evidence("critical-refraction", new { Source = model, Evaluation = failed, Result = result });
    }

    [Fact]
    public async Task CancellationInsideAnAutomaticGradientRetainsMeasuredPartialWork()
    {
        var source = Source(3); var binding = Bind(source, allShapes: true);
        var objective = new FixedTopologyObjective(source, binding, new(), new()
        { FieldsPerDirection = 21, PupilRings = 10, PupilArms = 32 });
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var measurement = SequentialTraceMeasurement.Begin();
        var evaluation = Task.Run(() => objective.Evaluate(binding.InitialCoordinates, cancellationToken: cancel.Token));
        while (!evaluation.IsCompleted && measurement.RayCount == 0 && !cancel.IsCancellationRequested) await Task.Delay(1);
        cancel.Cancel();
        var result = await evaluation.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.Available); Assert.Null(result.Objective);
        Assert.True(result.TracedRayCount > 0);
        Assert.Equal(measurement.RayCount, result.TracedRayCount);
        Evidence("partial-gradient-cancellation", result);
    }

    [Fact]
    public void IndependentValidationIsReservedWhenThereIsNoTrainingBudget()
    {
        var source = Source(2);
        var result = FixedTopologyOptimizer.Run(source, Bind(source), new(), new() { MaximumFunctionEvaluations = 1, ComputeMode = DesignComputeMode.Cpu });
        Assert.Equal(FixedTopologyTermination.BudgetExhausted, result.Termination);
        Assert.Equal(1, result.Work.FunctionEvaluations); Assert.Equal(0, result.Work.GradientEvaluations);
        Assert.Equal(1, result.Work.ValidationEvaluations);
        Assert.True(result.FinalAssessment!.Completed);
        Assert.Equal(source.TopologyFingerprint, ContentFingerprint.Compute(result.Topology));
    }

    [Fact]
    public void CancellationBeforeDispatchUsesNoOpticalWorkAndDuringUpdatesRetainsTheLastChargedState()
    {
        var source = Source(3); var binding = Bind(source);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var before = FixedTopologyOptimizer.Run(source, binding, new(), cancellationToken: cancelled.Token);
        Assert.Equal(FixedTopologyTermination.Cancelled, before.Termination);
        Assert.Equal(0, before.Work.FunctionEvaluations); Assert.Equal(0, before.Work.TracedRays);
        Assert.Null(before.FinalAssessment);
        using var cancel = new CancellationTokenSource();
        var after = FixedTopologyOptimizer.Run(source, binding, new(), new() { MaximumUpdates = 10, ComputeMode = DesignComputeMode.Cpu, Sampling = Small }, cancel.Token,
            step => { if (step.Accepted) cancel.Cancel(); });
        Assert.Equal(FixedTopologyTermination.Cancelled, after.Termination);
        Assert.Equal(2, after.Work.FunctionEvaluations); Assert.Equal(2, after.Work.GradientEvaluations);
        Assert.True(after.Work.TracedRays > 0);
        Assert.Equal(1, after.AcceptedUpdates); Assert.Null(after.FinalAssessment);
        var expected = after.InitialEvaluation!.TracedRayCount + after.EndpointEvaluation!.TracedRayCount;
        Assert.Equal(expected, after.Work.TracedRays);
        Evidence("cancellation", new { Before = before, After = after });
    }

    [Fact]
    public void ChangedTargetsSamplingAndBindingsCannotShareAnObjectiveIdentity()
    {
        var source = Source(3); var binding = Bind(source);
        var a = new FixedTopologyObjective(source, binding, new(), Small);
        var b = new FixedTopologyObjective(source, binding, new() { FNumber = 10 }, Small);
        var c = new FixedTopologyObjective(source, binding, new(), Small with { PupilRings = 2 });
        Assert.NotEqual(a.Fingerprint, b.Fingerprint); Assert.NotEqual(a.Fingerprint, c.Fingerprint);
        Assert.Throws<TopologyValidationException>(() => new FixedTopologyObjective(Source(2), binding, new()));
        var noAiming = TopologyCompiler.Compile(source.Topology with { RayAimingEnabled = false });
        var unsupported = FixedTopologyOptimizer.Run(noAiming, Bind(noAiming), new());
        Assert.Equal(FixedTopologyTermination.Unsupported, unsupported.Termination);
        Assert.Equal(0, unsupported.Work.FunctionEvaluations);
    }

    [Fact]
    public void ExpiredTimeAndUnassessableFieldSetsAreStoppedBeforeDispatch()
    {
        var source = Source(3);
        var timeout = FixedTopologyOptimizer.Run(source, Bind(source), new(), new() { TimeLimit = TimeSpan.FromTicks(1) });
        Assert.Equal(FixedTopologyTermination.TimeLimit, timeout.Termination);
        Assert.Equal(0, timeout.Work.FunctionEvaluations); Assert.Equal(0, timeout.Work.TracedRays);
        var manyDirections = source.Topology with
        {
            Fields = Enumerable.Range(0, 6).Select(index =>
            new TopologyField("direction-" + index, 5 * Math.Cos(index), 5 * Math.Sin(index))).ToImmutableArray()
        };
        var compiled = TopologyCompiler.Compile(manyDirections);
        var unsupported = FixedTopologyOptimizer.Run(compiled, Bind(compiled), new());
        Assert.Equal(FixedTopologyTermination.Unsupported, unsupported.Termination);
        Assert.Equal(0, unsupported.Work.FunctionEvaluations);
    }

    [Fact]
    public void ACompletelyBoundedStepStopsWithoutChargingFictitiousUpdates()
    {
        var source = Source(3);
        var curvature = source.Topology.Elements[0].Front.Profile.CurvaturePerMillimeter;
        var binding = TopologyParameterBinder.Bind(source, [new("lens-1:front", OpticalDesignParameterKind.Curvature, curvature, curvature)]);
        var result = FixedTopologyOptimizer.Run(source, binding, new(), new()
        {
            MaximumUpdates = 3,
            MaximumFunctionEvaluations = 5,
            Sampling = Small,
            ComputeMode = DesignComputeMode.Cpu
        });
        Assert.Equal(FixedTopologyTermination.Stalled, result.Termination);
        Assert.Equal(0, result.AcceptedUpdates);
        Assert.Equal(1, result.Work.GradientEvaluations);
        Assert.Equal(2, result.Work.FunctionEvaluations);
        Assert.True(result.FinalAssessment!.Completed);
    }

    [Fact]
    public void FixedImageAndProtectedLensRemainUnchangedThroughARealOptimizationStage()
    {
        var model = Source(3).Topology;
        model = model with
        {
            Image = model.Image with { Protected = true },
            Elements = model.Elements.SetItem(1, model.Elements[1] with { Protected = true })
        };
        var source = TopologyCompiler.Compile(model);
        var result = FixedTopologyOptimizer.Run(source, Bind(source), new(), new()
        {
            MaximumUpdates = 3,
            MaximumFunctionEvaluations = 5,
            ComputeMode = DesignComputeMode.Cpu,
            Sampling = Small
        });
        Assert.Equal(3, result.AcceptedUpdates);
        Assert.Equal(model.Image, result.Topology.Image);
        Assert.Equal(model.Elements[1], result.Topology.Elements[1]);
        Assert.True(result.FinalAssessment!.Completed, result.FinalAssessment.Failure);
        Evidence("protected-stage", result);
    }

    [Fact]
    public void RejectedAxialStepsKeepTheLastValidPrescriptionAndChargeEveryAttempt()
    {
        var source = Source(3);
        var binding = TopologyParameterBinder.Bind(source,
            [new("lens-1:front", OpticalDesignParameterKind.AxialPosition, -200, 200, 100)]);
        var result = FixedTopologyOptimizer.Run(source, binding, new(), new()
        {
            MaximumUpdates = 3,
            MaximumFunctionEvaluations = 5,
            LearningRate = 1,
            ComputeMode = DesignComputeMode.Cpu,
            Sampling = Small
        });
        Assert.Equal(3, result.Steps.Length);
        Assert.All(result.Steps, step => { Assert.False(step.Accepted); Assert.NotNull(step.Failure); });
        Assert.Equal(0, result.AcceptedUpdates);
        Assert.Equal(5, result.Work.FunctionEvaluations); Assert.Equal(4, result.Work.GradientEvaluations);
        Assert.Equal(1, result.Work.CompletedGradientEvaluations);
        Assert.Equal(source.TopologyFingerprint, ContentFingerprint.Compute(result.Topology));
        Assert.Equal(result.InitialEvaluation!.TracedRayCount + result.FinalAssessment!.TracedRayCount, result.Work.TracedRays);
        Evidence("rejected-steps", result);
    }

    [Fact]
    public void CudaStageMatchesTheCpuStageAndDisclosesItsActualWork()
    {
        if (!DesignGpu.Status.IsAvailable) { output.WriteLine("CUDA unavailable: " + DesignGpu.Status.UnavailableReason); return; }
        var source = Source(3); var binding = Bind(source);
        var options = new FixedTopologyOptions { MaximumUpdates = 2, MaximumFunctionEvaluations = 4, Sampling = Small, ComputeMode = DesignComputeMode.Cpu };
        var cpu = FixedTopologyOptimizer.Run(source, binding, new(), options);
        var gpu = FixedTopologyOptimizer.Run(source, binding, new(), options with { ComputeMode = DesignComputeMode.Gpu });
        Assert.Equal(2, gpu.AcceptedUpdates);
        Assert.True(gpu.Work.GpuDerivativeRays > 0); Assert.True(gpu.Work.GpuBatches > 0);
        Assert.NotNull(gpu.EndpointEvaluation!.PropagationExecution);
        Assert.True(gpu.EndpointEvaluation.PropagationExecution!.PropagatedRays > 0);
        Assert.InRange(Math.Abs(cpu.EndpointEvaluation!.Objective!.Value - gpu.EndpointEvaluation.Objective!.Value), 0, 1e-5);
        Assert.Equal(cpu.FinalAssessment!.MeetsTargets, gpu.FinalAssessment!.MeetsTargets);
        Assert.InRange(Math.Abs(cpu.FinalAssessment.EffectiveFocalLengthMillimeters!.Value - gpu.FinalAssessment.EffectiveFocalLengthMillimeters!.Value), 0, 1e-7);
        output.WriteLine($"CUDA stage: {gpu.Work.GpuDerivativeRays} derivative rays, {gpu.Work.GpuBatches} batches");
        Evidence("cuda-stage", new { Cpu = cpu, Gpu = gpu });
    }

    private static void AssertSameFormalAssessment(CompiledTopology source, TopologyAssessment actual)
    {
        var optic = source.CreateOptic();
        Assert.Equal(optic.Paraxial.EstimateEffectiveFocalLength(), actual.EffectiveFocalLengthMillimeters!.Value, 11);
        Assert.Equal(optic.Paraxial.EstimateFNumber(), actual.FNumber!.Value, 11);
        var samples = ApertureSampler.GenerateGaussianQuadrature(10, 32).ToArray();
        foreach (var field in actual.Fields)
        {
            var direct = SpotMetricEvaluator.EvaluatePupilSamples(optic, field.Field.X, field.Field.Y, samples, includeSurfaceTransmission: false);
            Assert.Equal(direct.Metrics!.RmsSpotRadius, field.RmsRadiusMillimeters!.Value, 11);
            Assert.Equal(direct.RayCount - direct.VignettedRayCount, field.ValidRays);
        }
    }

    private static CompiledTopology Source(int count)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        var path = Path.Combine(directory!.FullName, "labs/InitialStructure/benchmarks/autonomous-topology-v1", count == 2 ? "foundation-2.json" : "foundation-3.json");
        var model = JsonSerializer.Deserialize<DesignTopology>(File.ReadAllText(path), JsonOptions)!;
        model = model with { Image = model.Image with { Protected = false } };
        if (count == 4)
        {
            var seed = model.Elements[0] with
            {
                Id = "fourth",
                FrontSurfaceId = "fourth:front",
                BackSurfaceId = "fourth:back",
                Front = model.Elements[1].Front with { Profile = new() { CurvaturePerMillimeter = 1.0 / 150 } },
                Back = model.Elements[1].Back with { Profile = new() { CurvaturePerMillimeter = -1.0 / 150 } }
            };
            model = TopologyOperators.Insert(model, "lens-3:back", "image", seed, .2).Topology!;
        }
        return TopologyCompiler.Compile(model);
    }

    private static BoundTopologyParameters Bind(CompiledTopology source, bool allShapes = false)
    {
        var requests = new List<TopologyVariableRequest>();
        foreach (var element in source.Topology.Elements.Where(element => !element.Protected))
            foreach (var face in new[] { (Id: element.FrontSurfaceId, Face: element.Front, Z: element.FrontZMillimeters),
                (Id: element.BackSurfaceId, Face: element.Back, Z: element.BackZMillimeters) })
            {
                requests.Add(new(face.Id, OpticalDesignParameterKind.Curvature, -.1, .1, .01));
                if (!allShapes) continue;
                requests.Add(new(face.Id, OpticalDesignParameterKind.AxialPosition, face.Z - .25, face.Z + .25));
                requests.Add(new(face.Id, OpticalDesignParameterKind.Conic, -2, 2));
                if (face.Face.Profile.Kind == TopologyProfileKind.EvenAsphere)
                    for (var power = 4; power <= face.Face.Profile.MaximumEvenPower; power += 2)
                        requests.Add(new(face.Id, OpticalDesignParameterKind.EvenAsphereCoefficient, -1, 1, Math.Pow(10, -power), power));
            }
        if (!source.Topology.Image.Protected) requests.Add(new("image", OpticalDesignParameterKind.AxialPosition, 30, 250, 10));
        return TopologyParameterBinder.Bind(source, requests);
    }

    private static void Evidence(string name, object value)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_SYSTEM_DESIGN_TOPOLOGY_M2_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value, JsonOptions));
    }
}
