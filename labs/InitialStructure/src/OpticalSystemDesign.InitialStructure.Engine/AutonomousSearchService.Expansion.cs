using System.Collections.Immutable;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class AutonomousSearchService
{
    private static (string Operation, TopologyEditResult Edit) ExpandedEdit(AutonomousSearchCandidate parent, int branch, string id,
        AutonomousSearchPlan plan, Func<int, TopologyEditResult> insertion)
    {
        var options = plan.Options.Expansion!;
        var operations = new List<string>();
        var count = parent.Topology.Elements.Length;
        if (parent.Assessment.MeetsTargets)
        {
            if (options.EnableDeletion && count > plan.Constraints.MinimumElementCount) operations.Add("delete");
            if (options.EnableMerge && count > plan.Constraints.MinimumElementCount) operations.Add("merge");
        }
        else
        {
            if (options.EnableInsertion && count < plan.Constraints.MaximumElementCount) operations.Add("insert");
            if (options.EnableSplit && count < plan.Constraints.MaximumElementCount) operations.Add("split");
        }
        if (options.EnableMaterial) operations.Add("material");
        if (options.EnableProfile) operations.Add("profile");
        if (operations.Count == 0) return ("expanded", Rejected("No enabled structural/material/profile operation is legal at this count."));
        var operation = operations[branch % operations.Count];
        var variant = branch / operations.Count;
        var elements = parent.Topology.Elements.Where(element => !element.Protected).ToArray();
        if (elements.Length == 0) return (operation, Rejected("No unprotected lens element is available."));
        var element = elements[variant % elements.Length];
        var result = operation switch
        {
            "insert" => insertion(variant),
            "delete" => TopologyOperators.Delete(parent.Topology, element.Id, plan.Constraints),
            "split" => TopologyOperators.Split(parent.Topology, element.Id, id + "-a", id + "-b",
                Math.Max(plan.Constraints.MinimumAirGapMillimeters, options.SplitAirGapMillimeters), options.SplitFirstFraction, plan.Constraints),
            "merge" => Merge(),
            "material" => Material(),
            _ => Profile()
        };
        return (operation, result);

        TopologyEditResult Merge()
        {
            var index = variant % (count - 1);
            var left = parent.Topology.Elements[index]; var right = parent.Topology.Elements[index + 1];
            var material = options.MaterialAlternatives[variant % options.MaterialAlternatives.Length];
            return TopologyOperators.Merge(parent.Topology, left.Id, right.Id, id + "-merged", material, plan.Constraints);
        }
        TopologyEditResult Material()
        {
            var alternatives = options.MaterialAlternatives.Where(material => !string.Equals(material, element.Material, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (alternatives.Length == 0) return Rejected("No different permitted catalog material remains.");
            return TopologyOperators.ChangeMaterial(parent.Topology, element.Id, alternatives[(variant / elements.Length) % alternatives.Length], plan.Constraints);
        }
        TopologyEditResult Profile()
        {
            var faces = elements.SelectMany(item => new[] { (Id: item.FrontSurfaceId, item.Front.Profile), (Id: item.BackSurfaceId, item.Back.Profile) })
                .Where(face => face.Profile.Kind != TopologyProfileKind.EvenAsphere || face.Profile.MaximumEvenPower < options.MaximumEvenPower).ToArray();
            if (faces.Length == 0) return Rejected("No surface has an untried supported even-order upgrade.");
            var face = faces[variant % faces.Length];
            var order = face.Profile.Kind == TopologyProfileKind.EvenAsphere ? face.Profile.MaximumEvenPower + 2 : 4;
            return TopologyOperators.UpgradeProfile(parent.Topology, face.Id, order, plan.Constraints);
        }
        TopologyEditResult Rejected(string message) => new(null, null, new(TopologyFailureCode.UnsupportedFeature, message));
    }

    private static TopologyConstraints PhaseConstraints(AutonomousSearchPending pending, AutonomousSearchPlan plan)
        => pending.GradualDeletion is { } gradual ? TopologyOperators.DeletionConstraints(gradual, plan.Constraints, plan.Targets) : plan.Constraints;

    private static (AutonomousSearchPending? Pending, string Detail) BeginGradualDeletion(AutonomousSearchPending pending,
        DesignTopology baseline, AutonomousSearchPlan plan)
    {
        try
        {
            var removed = pending.Change?.RemovedElementIds;
            if (removed is not { Length: 1 }) return (null, "No single-element deletion origin is available; gradual fallback rejected.");
            var gradual = TopologyOperators.CreateGradualDeletion(baseline, removed.Value[0], plan.Options.Expansion!.DeletionScales, plan.Constraints);
            var edit = TopologyOperators.ApplyDeletionStage(gradual, baseline, plan.Constraints, plan.Targets);
            return edit.Succeeded ? (Fresh(pending, gradual, edit), "Direct recovery failed; beginning original-relative gradual deletion.")
                : (null, "Gradual initialization rejected: " + edit.Failure?.Message);
        }
        catch (TopologyValidationException error) { return (null, "Gradual initialization rejected: " + error.Message); }
    }

    private static (AutonomousSearchPending? Pending, string Detail) AdvanceGradualDeletion(AutonomousSearchPending pending,
        DesignTopology compensated, AutonomousSearchPlan plan)
    {
        var gradual = pending.GradualDeletion! with { StageIndex = pending.GradualDeletion.StageIndex + 1 };
        var edit = TopologyOperators.ApplyDeletionStage(gradual, compensated, plan.Constraints, plan.Targets);
        return edit.Succeeded ? (Fresh(pending, gradual, edit), gradual.StageIndex == gradual.Scales.Length
            ? "Target physically deleted; restoring original constraints for recovery, compensation and independent final validation."
            : "Transition is feasible under its scoped exception; advancing from the immutable original element.")
            : (null, "Next original-relative deletion stage rejected: " + edit.Failure?.Message);
    }

    private static AutonomousSearchPending Fresh(AutonomousSearchPending pending, GradualDeletionState gradual, TopologyEditResult edit)
        => pending with
        {
            Operation = "gradual-delete",
            GradualDeletion = gradual,
            Template = edit.Topology!,
            Change = edit.Change,
            Phase = AutonomousSearchPhase.Recovery,
            AfterFeasibility = null,
            Variables = [],
            StageOptions = null,
            Continuation = null,
            PhaseConsumed = new()
        };

    private static ImmutableArray<TopologyVariableRequest> ExpansionVariables(AutonomousSearchPending pending, AutonomousSearchOptions policy)
    {
        var frozenId = pending.GradualDeletion?.OriginalElement.Id;
        var topology = pending.Template;
        var requests = Variables(topology, policy.CurvatureBoundPerMillimeter).Where(request => !topology.Elements.Any(element => element.Id == frozenId
            && (element.FrontSurfaceId == request.SurfaceId || element.BackSurfaceId == request.SurfaceId))).ToList();
        requests.AddRange(AxialVariables(pending, policy));
        var profile = policy.Expansion is { EnableProfile: true };
        var options = policy.Expansion;
        if (!profile && policy.CompensateConic != true) return requests.ToImmutableArray();
        foreach (var element in topology.Elements.Where(element => !element.Protected && element.Id != frozenId))
            foreach (var (surfaceId, face) in new[] { (element.FrontSurfaceId, element.Front), (element.BackSurfaceId, element.Back) })
            {
                if (face.Profile.Kind == TopologyProfileKind.Plane) continue;
                var conicBound = Math.Max(options?.MaximumAbsoluteConic ?? 2, Math.Abs(face.Profile.Conic) * 2);
                requests.Add(new(surfaceId, OpticalDesignParameterKind.Conic, -conicBound, conicBound));
                if (!profile || face.Profile.Kind != TopologyProfileKind.EvenAsphere) continue;
                var radius = face.MechanicalSemiDiameterMillimeters ?? face.ClearSemiDiameterMillimeters;
                for (var power = 4; power <= face.Profile.MaximumEvenPower; power += 2)
                {
                    var initial = face.Profile.Terms.SingleOrDefault(term => term.Power == power)?.Coefficient ?? 0;
                    var bound = Math.Max(1e-30, Math.Max(options!.CoefficientCoordinateSagMillimeters / Math.Pow(radius, power), Math.Abs(initial) * 2));
                    requests.Add(new(surfaceId, OpticalDesignParameterKind.EvenAsphereCoefficient, -bound, bound, bound, power));
                }
            }
        return requests.ToImmutableArray();
    }

    private static void ValidateExpansion(TopologyExpansionOptions options, AutonomousSearchPlan plan)
    {
        if (plan.Constraints.AllowTransitionElements || plan.Constraints.TransitionAllowance is not null || plan.Seeds.Any(seed => seed.HasTransitionElements)
            || options.MaterialAlternatives.Length > 32 || (options.EnableMaterial || options.EnableMerge) && options.MaterialAlternatives.IsDefaultOrEmpty
            || options.MaterialAlternatives.Any(material => !Identifier(material))
            || !double.IsFinite(options.SplitAirGapMillimeters) || options.SplitAirGapMillimeters <= 0
            || !double.IsFinite(options.SplitFirstFraction) || options.SplitFirstFraction is <= 0 or >= 1
            || options.MaximumEvenPower is < 4 or > 18 || options.MaximumEvenPower % 2 != 0
            || !double.IsFinite(options.MaximumAbsoluteConic) || options.MaximumAbsoluteConic <= 0
            || !double.IsFinite(options.CoefficientCoordinateSagMillimeters) || options.CoefficientCoordinateSagMillimeters <= 0
            || options.DirectDeletionRecoveryMaximumFunctionEvaluations is < 1 or > 100_000
            || options.DeletionScales.Length is < 1 or > 8 || options.DeletionScales.Any(scale => !double.IsFinite(scale) || scale is <= 0 or >= 1)
            || !options.DeletionScales.Zip(options.DeletionScales.Skip(1), (a, b) => a > b).All(value => value))
            throw new ArgumentException("Invalid bounded expansion policy or non-original transition constraints.");
    }

    private static void ValidateExpansionCheckpoint(AutonomousSearchCheckpoint state)
    {
        foreach (var item in state.Events.Where(item => item.GradualDeletion is not null))
            TopologyOperators.ValidateGradualDeletion(item.GradualDeletion!);
        if (state.Pending is not { GradualDeletion: { } gradual } pending) return;
        TopologyOperators.ValidateGradualDeletion(gradual);
        if (state.Plan.Options.Expansion is not { EnableGradualDeletion: true } expansion || pending.Operation != "gradual-delete"
            || gradual.BaselineFingerprint != ContentFingerprint.Compute(state.Candidates.Single(candidate => candidate.Id == pending.ParentId).Topology)
            || !gradual.Scales.SequenceEqual(expansion.DeletionScales)
            || !state.Events.Any(item => item.TrialId == pending.TrialId && item.Operation == "delete" && item.Recovery is { Termination: not ParaxialRecoveryTermination.Recovered })
            || pending.Variables.Any(request => request.SurfaceId == gradual.OriginalElement.FrontSurfaceId || request.SurfaceId == gradual.OriginalElement.BackSurfaceId))
            throw new InvalidDataException("Gradual deletion origin, failed direct attempt, schedule or frozen variables changed.");
        var target = pending.Template.Elements.SingleOrDefault(element => element.Id == gradual.OriginalElement.Id);
        if (gradual.StageIndex == gradual.Scales.Length)
        {
            if (target is not null || pending.Template.HasTransitionElements || pending.Phase == AutonomousSearchPhase.TransitionValidation)
                throw new InvalidDataException("Physical deletion must clear the target and all transition exceptions.");
        }
        else if (target is null || pending.Phase == AutonomousSearchPhase.Validation
            || ContentFingerprint.Compute(target) != ContentFingerprint.Compute(TopologyOperators.PrescribedDeletionElement(gradual))
            || pending.Template.Elements.Any(element => element.Transition && element.Id != target.Id))
            throw new InvalidDataException("Transition target differs from its exact unscaled-origin prescription.");
    }
}
