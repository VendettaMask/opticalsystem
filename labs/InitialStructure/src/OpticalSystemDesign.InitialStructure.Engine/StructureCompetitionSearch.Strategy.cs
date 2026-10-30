using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal static partial class StructureCompetitionSearch
{
    internal static ImmutableArray<ImmutableArray<int>> PowerSignPatterns(int elements, int maximum)
    {
        if (elements is < 2 or > 16 || maximum is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(elements));
        var patterns = new List<ImmutableArray<int>>();
        void Add(IEnumerable<int> values)
        {
            var p = values.ToImmutableArray();
            if (patterns.Count < maximum && !patterns.Any(s => s.SequenceEqual(p))) patterns.Add(p);
        }
        // Full Gray-code enumeration for <=4; larger systems get bounded balanced,
        // alternating and single-negative coverage before the remaining Gray patterns.
        if (elements <= 4)
        {
            Add(Enumerable.Range(0, elements).Select(i => i == elements - 1 ? -1 : 1));
            Add(Enumerable.Range(0, elements).Select(i => i % 2 == 0 ? 1 : -1));
        }
        else
        {
            Add(Enumerable.Repeat(1, elements));
            Add(Enumerable.Range(0, elements).Select(i => i % 2 == 0 ? 1 : -1));
            Add(Enumerable.Range(0, elements).Select(i => i % 2 == 0 ? -1 : 1));
            for (var i = 0; i < elements; i++) { var k = i; Add(Enumerable.Range(0, elements).Select(j => j == k ? -1 : 1)); }
        }
        for (var i = 0; i < 1 << elements && patterns.Count < maximum; i++)
        {
            var gray = i ^ (i >> 1); Add(Enumerable.Range(0, elements).Select(j => (gray & (1 << j)) == 0 ? 1 : -1));
        }
        return patterns.ToImmutableArray();
    }

    private static StructureCandidateEvidence SourceEvidence(CompiledTopology source, int[]? requested)
    {
        var optic = source.CreateOptic();
        var powers = source.Elements.Select(e => optic.Paraxial.ElementOpticalPower(e.FrontIndex, e.BackIndex)).ToImmutableArray();
        return new(requested is null ? "NativeThickDelano" : "PowerSignThickDelano", requested?.ToImmutableArray() ?? [], powers,
            requested is null ? null : powers.Select((p, i) => double.IsFinite(p) && p * requested[i] > 1e-10).All(v => v));
    }

    private static (DesignTopology Topology, AutonomousSearchWork Work, StructureQuickEvidence Evidence) QuickOptimize(
        Candidate candidate, AutonomousSearchPlan plan, StructureCompetitionStage stage, int stageIndex,
        QuickAberrationOptions options, int maximumFunctions, long maximumRays, TimeSpan maximumTime,
        ref DeterministicRandom random, CancellationToken token)
    {
        var started = Stopwatch.StartNew();
        var staged = StageSpecification(candidate.Topology!, plan.Targets, stage);
        var source = TopologyCompiler.Compile(staged.Topology, plan.Constraints);
        var binding = TopologyParameterBinder.Bind(source, candidate.Variables);
        var coordinates = binding.InitialCoordinates.ToArray();
        int calls = 0, proposals = 0, accepted = 0, matrices = 0; long probes = 0;
        string outcome = "Completed";
        var eligible = binding.Slots.Select((s, i) => (s, i)).Where(x => x.s.Request.Kind == OpticalDesignParameterKind.Curvature).ToArray();
        using var time = CancellationTokenSource.CreateLinkedTokenSource(token); time.CancelAfter(maximumTime);
        using var rays = Core.Raytrace.SequentialTraceMeasurement.BeginCpuBudget(maximumRays);
        (bool Supported, bool Eligible, double? Score, string Outcome) current = (false, false, null, "NotEvaluated");
        double? initial = null;
        try
        {
            current = Evaluate(source); initial = current.Score;
            for (var i = 0; i < options.MaximumProposalsPerStage && calls < maximumFunctions && eligible.Length > 0 && current.Supported; i++)
            {
                token.ThrowIfCancellationRequested(); if (time.IsCancellationRequested) { outcome = "QuickTimeLimit"; break; }
                proposals++; var proposed = coordinates.ToArray(); var variable = eligible[random.NextInt32(eligible.Length)];
                proposed[variable.i] = Math.Clamp(proposed[variable.i] + (random.NextUnitDouble() < .5 ? -1 : 1) * options.CoordinateStep,
                    variable.s.LowerCoordinate, variable.s.UpperCoordinate);
                var trialSource = TopologyParameterBinder.Apply(source, binding, proposed);
                var trial = Evaluate(trialSource);
                if (trial.Eligible && trial.Supported && trial.Score < current.Score)
                { current = trial; coordinates = proposed; accepted++; }
            }
        }
        catch (OperationCanceledException) { outcome = token.IsCancellationRequested ? "Cancelled" : "QuickTimeLimit"; }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { outcome = "QuickDomainRejected:" + error.GetType().Name; }
        var endpoint = TopologyParameterBinder.Apply(source, binding, coordinates).Topology;
        return (endpoint with { Fields = candidate.Initial!.Fields, Stop = candidate.Initial.Stop },
            new() { FunctionEvaluations = calls, MatrixEvaluations = matrices, GeometryProbes = probes, TracedRays = rays.RayCount },
            new(candidate.Id, stageIndex, current.Supported ? outcome : current.Outcome, "waves", initial, current.Score, proposals, accepted));

        (bool Supported, bool Eligible, double? Score, string Outcome) Evaluate(CompiledTopology compiled)
        {
            calls++; time.Token.ThrowIfCancellationRequested();
            var optic = compiled.CreateOptic();
            var wave = compiled.Topology.Wavelengths.Single(w => w.IsPrimary).Nanometers / 1000;
            var quick = SeidelQuickEvaluator.Evaluate(optic, wave);
            matrices += 2;
            var focal = optic.Paraxial.EstimateEffectiveFocalLength(); var fNumber = optic.Paraxial.EstimateFNumber();
            var valid = double.IsFinite(focal) && focal > 0 && double.IsFinite(fNumber) && fNumber > 0
                && Math.Abs(focal / staged.Targets.EffectiveFocalLengthMillimeters - 1) <= staged.Targets.FocalLengthRelativeTolerance
                && Math.Abs(fNumber / staged.Targets.FNumber - 1) <= staged.Targets.FNumberRelativeTolerance;
            foreach (var row in TopologyEvaluationPlan.Geometry(compiled, staged.Targets, 64))
            {
                time.Token.ThrowIfCancellationRequested(); probes++;
                var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                valid &= double.IsFinite(value) && (row.Upper ? value <= row.Limit : value >= row.Limit);
            }
            return (quick.Supported, valid, quick.Score, quick.Outcome);
        }
    }
}
