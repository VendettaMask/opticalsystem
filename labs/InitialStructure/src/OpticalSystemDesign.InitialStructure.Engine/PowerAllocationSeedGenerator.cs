using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Alternative bounded starting route: perturb only permitted spacing, assign front element powers
/// through Core, freeze them, then close the remaining degrees with the existing Core-backed target solver.</summary>
internal static class PowerAllocationSeedGenerator
{
    private sealed class AllocationBudget : Exception;
    internal static (DesignTopology? Topology, PowerAllocationSeedEvidence Evidence) Run(string parentId, DesignTopology parent,
        AutonomousSearchPlan plan, ParaxialSeedSearchOptions preparation, PowerAllocationSeedOptions policy,
        int maximumFunctions, TimeSpan maximumTime, ref DeterministicRandom random, CancellationToken token)
    {
        var watch = Stopwatch.StartNew(); var functions = 0; var matrices = 0; var jacobians = 0; long probes = 0;
        var allocatedIds = new List<string>(); var requested = new List<double>(); var actual = new List<double>();
        string? closureIdentity = null; var outcome = "NoClosedSeed"; DesignTopology? endpoint = null;
        var preserved = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(maximumTime);
        try
        {
            var editable = parent.Elements.Where(e => !e.Protected).ToArray();
            if (editable.Length < 3) { outcome = "InsufficientAllocationAndClosureDegreesOfFreedom"; throw new AllocationBudget(); }
            var compiled = TopologyCompiler.Compile(parent, plan.Constraints);
            var variables = StructureEvolutionSearch.EvolutionVariables(parent, plan.Options);
            var binding = TopologyParameterBinder.Bind(compiled, variables);
            var coordinates = binding.InitialCoordinates.ToArray();
            foreach (var element in parent.Elements.Where(e => !e.Protected))
            {
                var indices = Enumerable.Range(0, coordinates.Length).Where(i => binding.Slots[i].Request.Kind == OpticalDesignParameterKind.AxialPosition
                    && (binding.Slots[i].Request.SurfaceId == element.FrontSurfaceId || binding.Slots[i].Request.SurfaceId == element.BackSurfaceId)).ToArray();
                if (indices.Length == 0) continue;
                var lower = indices.Max(i => binding.Slots[i].LowerCoordinate - coordinates[i]);
                var upper = indices.Min(i => binding.Slots[i].UpperCoordinate - coordinates[i]);
                var shift = lower + random.NextUnitDouble() * (upper - lower);
                foreach (var i in indices) coordinates[i] += shift;
            }
            compiled = TopologyParameterBinder.Apply(compiled, binding, coordinates);
            var chosen = editable.Take(editable.Length - 2).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var element in compiled.Topology.Elements.Where(e => chosen.Contains(e.Id)))
            {
                var map = compiled.Elements.Single(e => e.ElementId == element.Id);
                Charge(); matrices++;
                var currentPower = compiled.CreateOptic().Paraxial.ElementOpticalPower(map.FrontIndex, map.BackIndex);
                var limit = policy.MaximumAbsoluteNormalizedPower / plan.Targets.EffectiveFocalLengthMillimeters;
                var target = Math.Clamp(currentPower + (2 * random.NextUnitDouble() - 1) * policy.PerturbationFraction * limit, -limit, limit);
                allocatedIds.Add(element.Id); requested.Add(target);
                var assignment = ElementPowerAssignment.Assign(compiled.CreateOptic(), map.FrontIndex, map.BackIndex,
                    target, plan.Options.CurvatureBoundPerMillimeter, deadline.Token, () => { Charge(); matrices++; });
                if (!assignment.Succeeded) { outcome = assignment.Outcome; throw new AllocationBudget(); }
                compiled = TopologyCompiler.Update(compiled, assignment.Snapshot!);
            }
            var fixedElements = compiled.Topology.Elements.Where(e => chosen.Contains(e.Id)).ToDictionary(e => e.Id, StringComparer.Ordinal);
            var frozen = compiled.Topology with { Elements = compiled.Topology.Elements.Select(e => chosen.Contains(e.Id) ? e with { Protected = true } : e).ToImmutableArray() };
            var available = maximumFunctions - functions - fixedElements.Count - 1;
            if (available < 2) { outcome = "InsufficientTargetClosureBudget"; throw new AllocationBudget(); }
            var closed = ParaxialSeedSearch.Run(plan with { Seeds = [frozen] }, new()
            {
                MaximumStarts = 1,
                RetainedSeeds = 1,
                FixedStopRecoveryVersion = 2,
                MaximumFunctionEvaluations = Math.Min(policy.ClosureFunctionsPerAttempt, available),
                MaximumEvaluationsPerStart = Math.Min(policy.ClosureFunctionsPerAttempt, available),
                MaximumTracedRays = 10000,
                TimeLimit = maximumTime
            }, deadline.Token);
            functions += closed.Work.FunctionEvaluations; matrices += closed.MatrixRequests;
            jacobians += closed.Work.JacobianBuilds; probes += closed.Work.GeometryProbes; closureIdentity = closed.ProtocolFingerprint;
            if (closed.Seeds.IsEmpty) { outcome = "TargetClosure:" + closed.Termination; throw new AllocationBudget(); }
            var restored = closed.Seeds[0] with { Elements = closed.Seeds[0].Elements.Select(e => fixedElements.GetValueOrDefault(e.Id) ?? e).ToImmutableArray() };
            compiled = TopologyCompiler.Compile(restored, plan.Constraints); var optic = compiled.CreateOptic();
            foreach (var id in allocatedIds)
            {
                Charge(); matrices++; var map = compiled.Elements.Single(e => e.ElementId == id);
                actual.Add(optic.Paraxial.ElementOpticalPower(map.FrontIndex, map.BackIndex));
            }
            preserved = requested.Zip(actual).All(p => double.IsFinite(p.Second) && Math.Abs(p.First - p.Second) < 1e-10);
            Charge(); matrices += 3;
            var focal = optic.Paraxial.EstimateEffectiveFocalLength(); var fNumber = optic.Paraxial.EstimateFNumber();
            var transfer = optic.Paraxial.GetTransferMatrix(compiled.SurfaceIndices[restored.Reference.Id], compiled.SurfaceIndices[restored.Image.Id]);
            var closedTargets = double.IsFinite(focal) && focal > 0 && double.IsFinite(fNumber) && fNumber > 0
                && Math.Abs(focal / plan.Targets.EffectiveFocalLengthMillimeters - 1) <= plan.Targets.FocalLengthRelativeTolerance
                && Math.Abs(fNumber / plan.Targets.FNumber - 1) <= plan.Targets.FNumberRelativeTolerance
                && Math.Abs(transfer.A) <= preparation.FocusATolerance;
            if (preserved && closedTargets) { endpoint = restored; outcome = "AllocatedPowersAndOriginalTargetsClosed"; }
            else outcome = "IndependentClosureVerificationFailed";
        }
        catch (AllocationBudget) { }
        catch (OperationCanceledException) { outcome = token.IsCancellationRequested ? "Cancelled" : "PowerAllocationTimeLimit"; }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { outcome = "PowerAllocationDomain:" + error.Message; }
        var work = new AutonomousSearchWork
        {
            FunctionEvaluations = functions,
            MatrixEvaluations = matrices,
            JacobianBuilds = jacobians,
            GeometryProbes = probes,
            Elapsed = watch.Elapsed
        };
        return (endpoint, new(parentId, outcome, allocatedIds.ToImmutableArray(), requested.ToImmutableArray(), actual.ToImmutableArray(),
            preserved, closureIdentity, work));

        void Charge() { deadline.Token.ThrowIfCancellationRequested(); if (functions >= maximumFunctions) { outcome = "PowerAllocationFunctionBudget"; throw new AllocationBudget(); } functions++; }
    }
}
