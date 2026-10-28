using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Tests;

public sealed class PrimaryFirstCandidateTests
{
    [Fact]
    public void PrimaryPathPrecedesLazyFallbackAndSurvivesResume()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        var json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        json.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName,
            "validation/rechecks/2026-10-28/two-element-candidate-chain/candidate-chain-input.json")));
        T Read<T>(string name) => JsonSerializer.Deserialize<T>(input.RootElement.GetProperty(name).GetRawText(), json)!;
        var source = TopologyCompiler.Compile(Read<DesignTopology>("Source"), Read<TopologyConstraints>("Constraints"));
        var binding = TopologyParameterBinder.Bind(source, Read<ImmutableArray<TopologyVariableRequest>>("Variables"));
        var options = Read<FixedTopologyOptions>("Full") with { TransmissionPolicyVersion = 14,
            MaximumUpdates = 3, MaximumFunctionEvaluations = 4, MaximumGradientEvaluations = 4, PerformFinalAssessment = false };
        using var quota = SequentialTraceMeasurement.BeginCpuBudget(250_000);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        FixedTopologyResult? full = null, part = null, resumed = null;
        try
        {
            full = FixedTopologyOptimizer.Run(source, binding, Read<TopologyTargets>("Targets"), options, deadline.Token);
            Assert.Equal(new bool?[] { false, true, false }, full.Steps.Select(s => s.UsedFallbackCandidate));
            Assert.Equal(new bool?[] { false, true, false }, full.Steps.Select(s => s.FallbackConsidered));
            Assert.Equal(new[] { false, false, true }, full.Steps.Select(s => s.Accepted));
            Assert.DoesNotContain(full.Diagnostics, s => s.StartsWith("Projection attempt 1:") && s.Contains("fallbackAttempted=True"));
            Assert.False(full.Continuation!.SuppressFallbackAtCurrentPoint);
            Assert.False(full.Continuation.TryFallbackNext);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            part = FixedTopologyOptimizer.Run(source, binding, Read<TopologyTargets>("Targets"), options, stop.Token,
                step => { if (step.Attempt == 1) stop.Cancel(); });
            var saved = JsonSerializer.Deserialize<FixedTopologyContinuation>(JsonSerializer.Serialize(part.Continuation, json), json)!;
            Assert.True(saved.TryFallbackNext);
            Assert.False(saved.SuppressFallbackAtCurrentPoint);
            var beforeTamper = quota.RayCount;
            Assert.Throws<TopologyValidationException>(() => FixedTopologyOptimizer.Run(source, binding, Read<TopologyTargets>("Targets"),
                options, deadline.Token, continuation: saved with { TryFallbackNext = false }));
            Assert.Equal(beforeTamper, quota.RayCount);
            resumed = FixedTopologyOptimizer.Run(source, binding, Read<TopologyTargets>("Targets"), options, deadline.Token, continuation: saved);
            Assert.Equal(ContentFingerprint.Compute(full.Topology), ContentFingerprint.Compute(resumed.Topology));
            Assert.Equal(full.Work.TracedRays, resumed.Work.TracedRays);
            Assert.Equal(full.Work.FunctionEvaluations, resumed.Work.FunctionEvaluations);
            Assert.Equal(full.Work.GradientEvaluations, resumed.Work.GradientEvaluations);
        }
        finally
        {
            if (Environment.GetEnvironmentVariable("AUTOLENS_PRIMARY_EVIDENCE") is { } folder)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "reproduction.json"), JsonSerializer.Serialize(new { full, part, resumed,
                    Rays = quota.RayCount, RayCap = 250_000, TimeCapSeconds = 60 }, json));
            }
        }
    }
}
