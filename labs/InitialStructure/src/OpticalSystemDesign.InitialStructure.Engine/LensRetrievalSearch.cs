using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Finite retrieval of existing prescriptions. Compatibility and ranking never replace original physical acceptance.</summary>
public static class LensRetrievalSearch
{
    private sealed class Limit : Exception;
    public static void ValidateOptions(ParaxialSeedSearchOptions options)
    {
        var retrieval = options.Retrieval!;
        if (retrieval.Version != 1 || options.Evolution is not null || options.Competition is not null || options.Generation is not null
            || options.ImageScreening is not null || options.FixedStopRecoveryVersion is not null
            || retrieval.MaximumCandidates is < 1 or > 8 || retrieval.MaximumCandidates > options.MaximumStarts
            || !double.IsFinite(retrieval.MinimumScale) || !double.IsFinite(retrieval.MaximumScale)
            || retrieval.MinimumScale < .001 || retrieval.MaximumScale > 1000 || retrieval.MaximumScale < retrieval.MinimumScale
            || !double.IsFinite(retrieval.MaximumSourceSlowerFraction) || retrieval.MaximumSourceSlowerFraction is < 0 or > 1
            || !double.IsFinite(retrieval.FinalRayRecoveryFactor) || retrieval.FinalRayRecoveryFactor is < 1 or > 20)
            throw new ArgumentException("Retrieval v1 requires an exclusive, bounded library route and explicit scale/final-validation limits.");
        LensLibraryService.Validate(retrieval.Library);
    }

    public static ParaxialSeedSearchResult Run(AutonomousSearchPlan plan, ParaxialSeedSearchOptions options,
        CancellationToken cancellationToken = default, Action<int, int>? progress = null)
    {
        ParaxialSeedSearch.ValidateOptions(options);
        _ = AutonomousSearchService.Create("lens-retrieval-check", plan);
        if (AutonomousSearchService.IsHistoricalReadOnlyPlan(plan) || plan.Seeds.Any(seed => seed.HasTransitionElements))
            throw new ArgumentException("Retrieval requires an ordinary supported current task.");
        var policy = options.Retrieval!; var template = plan.Seeds[0];
        var templateSource = TopologyCompiler.Compile(template, plan.Constraints);
        var field = FieldCoordinates.MaximumRadius(templateSource.CreateOptic().Fields.Where(item => item.Weight > 0));
        var protocol = ContentFingerprint.Compute(new
        {
            Identity = "initial-structure-lens-retrieval-v1",
            plan,
            options,
            Transform = "uniform-Core-scale/stop-anchor/fixed-task-planes/ordinal-task-apertures-v1",
            Backend = "CPU/double"
        });
        var timer = Stopwatch.StartNew();
        using var deadline = new CancellationTokenSource(options.TimeLimit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token;
        using var rays = SequentialTraceMeasurement.BeginCpuBudget(options.MaximumTracedRays);
        using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
        var trials = policy.Library.Entries.Select(entry => new LensRetrievalTrial
        {
            EntryId = entry.Id,
            Provenance = entry.Provenance,
            EntryFingerprint = ContentFingerprint.Compute(entry),
            Outcome = "NotIndexed"
        }).ToArray();
        var eligible = new List<(int Index, double Score)>();
        var verifiedDescriptors = new Dictionary<int, CenteredPrescriptionDescriptor>();
        var functions = 0; var validations = 0; var descriptors = 0; long probes = 0;
        var termination = ParaxialSeedTermination.Completed; var reason = "Completed";
        try
        {
            for (var index = 0; index < trials.Length; index++)
            {
                Check(); var before = timer.Elapsed; functions++; descriptors++;
                try
                {
                    var entry = policy.Library.Entries[index];
                    var source = TopologyCompiler.Compile(entry.Topology, entry.Constraints);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(source.OpticFingerprint, entry.SourceOpticFingerprint)) throw new ArgumentException("The source catalog or frozen prescription changed.");
                    var measured = CenteredPrescriptionScaling.Describe(source.CreateOptic(), source.Elements[^1].BackIndex);
                    if (!SameDescriptor(measured, entry.Descriptor)) throw new ArgumentException("The indexed source descriptor is stale or altered.");
                    verifiedDescriptors.Add(index, measured);
                    string? rejection = entry.Topology.Elements.Length != template.Elements.Length ? "FixedApertureMappingRequiresSameCount"
                        : entry.Topology.CementedInterfaces is not null ? "CementedSchedulingUnsupported"
                        : entry.Topology.Elements.Any(element => element.Protected) ? "ProtectedLibraryPrescription"
                        : measured.MaximumHalfFieldDegrees < field ? "SourceFieldTooSmall"
                        : measured.FNumber > plan.Targets.FNumber * (1 + policy.MaximumSourceSlowerFraction) ? "SourcePupilTooSlow" : null;
                    var scale = ScaleFor(measured, plan.Targets);
                    if (rejection is null && (scale < policy.MinimumScale || scale > policy.MaximumScale)) rejection = "ScaleOutsideBounds";
                    var score = Math.Abs(Math.Log(scale)) + Math.Abs(Math.Log(measured.FNumber / plan.Targets.FNumber));
                    trials[index] = trials[index] with { Outcome = rejection ?? "IndexedCompatible", RankScore = score };
                    if (rejection is null) eligible.Add((index, score));
                }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error))
                { trials[index] = trials[index] with { Outcome = "SourceRejected", Detail = error.Message }; }
                finally { AddWork(index, 1, 0, 0, 0, timer.Elapsed - before); }
                progress?.Invoke(index + 1, functions);
            }
            var transformed = new List<int>();
            foreach (var item in eligible.OrderBy(item => item.Score).ThenBy(item => trials[item.Index].EntryId, StringComparer.Ordinal).Take(options.MaximumStarts))
            {
                if (trials[item.Index].Work.FunctionEvaluations >= options.MaximumEvaluationsPerStart)
                { trials[item.Index] = trials[item.Index] with { Outcome = "PerEntryFunctionBudget" }; continue; }
                Check(); var before = timer.Elapsed; functions++; descriptors++;
                try
                {
                    var (source, transform) = Transfer(policy.Library.Entries[item.Index] with { Descriptor = verifiedDescriptors[item.Index] }, templateSource, plan, policy);
                    trials[item.Index] = trials[item.Index] with
                    {
                        Topology = source.Topology,
                        Transform = transform,
                        MatchesOriginalInput = plan.Seeds.Any(seed => SeedIdentity(seed) == SeedIdentity(source.Topology)),
                        Outcome = "Transformed"
                    };
                    var measured = CenteredPrescriptionScaling.Describe(source.CreateOptic(), source.Elements[^1].BackIndex);
                    if (Math.Abs(measured.EffectiveFocalLengthMillimeters / plan.Targets.EffectiveFocalLengthMillimeters - 1) > plan.Targets.FocalLengthRelativeTolerance
                        || Math.Abs(measured.FNumber / plan.Targets.FNumber - 1) > plan.Targets.FNumberRelativeTolerance)
                        trials[item.Index] = trials[item.Index] with { Outcome = "OriginalFirstOrderRejected" };
                    else transformed.Add(item.Index);
                }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error))
                { trials[item.Index] = trials[item.Index] with { Outcome = "TransferRejected", Detail = error.Message }; }
                finally { AddWork(item.Index, 1, 0, 0, 0, timer.Elapsed - before); }
            }
            foreach (var index in transformed.Take(policy.MaximumCandidates))
            {
                if (trials[index].Work.FunctionEvaluations >= options.MaximumEvaluationsPerStart)
                { trials[index] = trials[index] with { Outcome = "PerEntryFunctionBudget" }; continue; }
                Check(); var source = TopologyCompiler.Compile(trials[index].Topology!, plan.Constraints);
                if (options.MaximumTracedRays - rays.RayCount < StructureSearchDiagnostics.EstimateFinalRays(source, policy.FinalRayRecoveryFactor))
                { reason = "InsufficientCompleteValidationBudget"; termination = ParaxialSeedTermination.BudgetExhausted; break; }
                var before = timer.Elapsed; var beforeRays = rays.RayCount; functions++; validations++;
                var assessment = TopologyValidator.Validate(source, plan.Targets, token);
                probes += assessment.GeometryProbes;
                trials[index] = trials[index] with
                {
                    Assessment = assessment,
                    Outcome = !assessment.Completed ? "OriginalAssessmentIncomplete" : assessment.MeetsTargets ? "QualifiedRetrievedPrescription"
                        : StructureEvolutionSearch.IsWorkingSeedAssessment(assessment) ? "WorkingSeedImageTargetsFailed" : "OriginalAssessmentRejected"
                };
                AddWork(index, 1, 1, rays.RayCount - beforeRays, assessment.GeometryProbes, timer.Elapsed - before);
                if (!assessment.Completed) token.ThrowIfCancellationRequested();
                progress?.Invoke(trials.Length, functions);
            }
        }
        catch (Limit) { termination = ParaxialSeedTermination.BudgetExhausted; reason = "FunctionBudget"; }
        catch (OperationCanceledException) { termination = cancellationToken.IsCancellationRequested ? ParaxialSeedTermination.Cancelled : ParaxialSeedTermination.TimeLimit; reason = termination.ToString(); }
        var retained = trials.Where(trial => trial.Assessment is { } assessment && StructureEvolutionSearch.IsWorkingSeedAssessment(assessment))
            .OrderByDescending(trial => trial.Assessment!.MeetsTargets)
            .ThenBy(trial => trial.Assessment!.Fields.Max(f => Math.Max((f.RmsRadiusMillimeters ?? double.MaxValue) / plan.Targets.MaximumRmsRadiusMillimeters,
                (f.MaximumRadiusMillimeters ?? double.MaxValue) / plan.Targets.MaximumSpotRadiusMillimeters)))
            .ThenBy(trial => trial.EntryId, StringComparer.Ordinal).DistinctBy(trial => SeedIdentity(trial.Topology!)).Take(options.RetainedSeeds).ToArray();
        var known = policy.Library.Entries.GroupBy(entry => entry.Provenance.SourceDocumentSha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Select(entry => entry.SourceAcquisitionWork).OfType<AutonomousSearchWork>().FirstOrDefault()).OfType<AutonomousSearchWork>()
            .Aggregate(new AutonomousSearchWork(), Sum);
        return new()
        {
            Algorithm = "initial-structure-lens-retrieval-v1",
            ProtocolFingerprint = protocol,
            SourcePlan = plan,
            Options = options,
            Termination = termination,
            Seeds = retained.Select(trial => trial.Topology!).ToImmutableArray(),
            Attempts = trials.Select((trial, index) => new ParaxialSeedAttempt(index, trial.EntryFingerprint, trial.Outcome,
                trial.Work.FunctionEvaluations, trial.RankScore, trial.Topology)).ToImmutableArray(),
            Work = new() { FunctionEvaluations = functions, ValidationEvaluations = validations, TracedRays = rays.RayCount, GeometryProbes = probes, Elapsed = timer.Elapsed },
            Retrieval = new()
            {
                LibraryFingerprint = ContentFingerprint.Compute(policy.Library),
                StopReason = reason,
                Trials = trials.ToImmutableArray(),
                DescriptorEvaluations = descriptors,
                UnknownAcquisitionCosts = policy.Library.Entries.Count(entry => entry.SourceAcquisitionWork is null),
                KnownSourceAcquisitionWork = known,
                LibraryIndexingWork = policy.Library.Entries.Select(entry => entry.IndexingWork).Aggregate(new AutonomousSearchWork(), Sum),
                QualifiedEntryIds = trials.Where(trial => trial.Assessment is { Completed: true, MeetsTargets: true }).Select(trial => trial.EntryId).ToImmutableArray(),
                RetainedEntryIds = retained.Select(trial => trial.EntryId).ToImmutableArray()
            }
        };

        void Check() { token.ThrowIfCancellationRequested(); if (functions >= options.MaximumFunctionEvaluations) throw new Limit(); }
        void AddWork(int index, int f, int v, long r, long g, TimeSpan elapsed) => trials[index] = trials[index] with
        { Work = Sum(trials[index].Work, new() { FunctionEvaluations = f, ValidationEvaluations = v, TracedRays = r, GeometryProbes = g, Elapsed = elapsed }) };
    }

    private static (CompiledTopology, LensRetrievalTransform) Transfer(LensLibraryEntry entry, CompiledTopology taskSource,
        AutonomousSearchPlan plan, LensRetrievalOptions policy)
    {
        var task = taskSource.Topology;
        var scale = ScaleFor(entry.Descriptor, plan.Targets);
        var original = TopologyCompiler.Compile(entry.Topology, entry.Constraints);
        var scaled = entry.Topology;
        if (scale != 1)
        {
            var optic = CenteredPrescriptionScaling.Scale(original.Snapshot, scale);
            var bounds = entry.Constraints with
            {
                MinimumCenterThicknessMillimeters = entry.Constraints.MinimumCenterThicknessMillimeters * scale,
                MinimumAirGapMillimeters = entry.Constraints.MinimumAirGapMillimeters * scale,
                MinimumBackFocusMillimeters = entry.Constraints.MinimumBackFocusMillimeters * scale,
                MaximumMechanicalTrackMillimeters = entry.Constraints.MaximumMechanicalTrackMillimeters * scale
            };
            scaled = TopologyCompiler.Import(optic.ToSnapshot(), bounds, entry.Topology.Reference.ZMillimeters * scale).Topology;
        }
        var shift = task.Stop.ZMillimeters - scaled.Stop.ZMillimeters;
        var apertures = ImmutableArray.CreateBuilder<RetrievedFaceAperture>();
        var elements = task.Elements.Select((baseline, index) =>
        {
            var lens = scaled.Elements[index];
            if (original.Materials[index].ComponentFingerprint != taskSource.Materials[index].ComponentFingerprint
                && (plan.Options.Expansion?.EnableMaterial != true || !plan.Options.Expansion.MaterialAlternatives.Contains(lens.Material, StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("Material replacement is outside the original task permissions or alternative catalog set.");
            CheckProfile(baseline.Front, lens.Front); CheckProfile(baseline.Back, lens.Back);
            var changed = baseline with
            {
                FrontZMillimeters = lens.FrontZMillimeters + shift,
                ThicknessMillimeters = lens.ThicknessMillimeters,
                Material = lens.Material,
                Front = Face(baseline.FrontSurfaceId, baseline.Front, lens.Front),
                Back = Face(baseline.BackSurfaceId, baseline.Back, lens.Back)
            };
            if (baseline.Protected && ContentFingerprint.Compute(changed) != ContentFingerprint.Compute(baseline))
                throw new ArgumentException("The transfer would change a protected original lens.");
            var window = plan.Options.AxialCompensation is { } axial && (axial.ElementIds.IsEmpty || axial.ElementIds.Contains(baseline.Id))
                ? axial.MaximumDisplacementMillimeters : 0;
            if (Math.Abs(changed.FrontZMillimeters - baseline.FrontZMillimeters) > window
                || Math.Abs(changed.BackZMillimeters - baseline.BackZMillimeters) > window)
                throw new ArgumentException("The transfer exceeds the original per-element axial displacement permission.");
            return changed;
        }).ToImmutableArray();
        var source = TopologyCompiler.Compile(task with { Elements = elements }, plan.Constraints);
        if (!source.Materials.Select(item => item.ComponentFingerprint).SequenceEqual(original.Materials.Select(item => item.ComponentFingerprint)))
            throw new ArgumentException("The task catalog would substitute different material data.");
        return (source, new(scale, shift, scaled.Image.ZMillimeters + shift, task.Image.ZMillimeters, apertures.ToImmutable()));

        TopologyFace Face(string id, TopologyFace baseline, TopologyFace donor)
        {
            apertures.Add(new(id, donor.ClearSemiDiameterMillimeters, baseline.ClearSemiDiameterMillimeters, baseline.MechanicalSemiDiameterMillimeters));
            return baseline with { Profile = donor.Profile };
        }
        void CheckProfile(TopologyFace baselineFace, TopologyFace donorFace)
        {
            var baseline = baselineFace.Profile; var donor = donorFace.Profile;
            if (Math.Abs(donor.CurvaturePerMillimeter) > plan.Options.CurvatureBoundPerMillimeter
                || plan.Options.Expansion?.EnableProfile != true && (donor.Kind != baseline.Kind || donor.MaximumEvenPower > baseline.MaximumEvenPower)
                || plan.Options.CompensateConic != true && plan.Options.Expansion?.EnableProfile != true && donor.Conic != baseline.Conic)
                throw new ArgumentException("The retrieved profile exceeds the original shape permissions.");
            if (plan.Options.Expansion is { EnableProfile: true } expansion
                && (donor.MaximumEvenPower > expansion.MaximumEvenPower || Math.Abs(donor.Conic) > expansion.MaximumAbsoluteConic))
                throw new ArgumentException("The retrieved asphere exceeds the original explicit profile-order/conic limits.");
            if (plan.Options.Expansion?.EnableProfile != true && !baseline.Terms.OrderBy(term => term.Power).SequenceEqual(donor.Terms.OrderBy(term => term.Power)))
                throw new ArgumentException("The original task does not permit replacing polynomial coefficients.");
            if (plan.Options.Expansion is { EnableProfile: true } polynomial)
                foreach (var term in donor.Terms)
                {
                    var existing = baseline.Terms.SingleOrDefault(t => t.Power == term.Power)?.Coefficient ?? 0;
                    var radius = baselineFace.MechanicalSemiDiameterMillimeters ?? baselineFace.ClearSemiDiameterMillimeters;
                    var bound = Math.Max(1e-30, Math.Max(2 * Math.Abs(existing), polynomial.CoefficientCoordinateSagMillimeters / Math.Pow(radius, term.Power)));
                    if (Math.Abs(term.Coefficient) > bound) throw new ArgumentException("A retrieved polynomial coefficient exceeds the original task scale bound.");
                }
        }
    }

    private static string SeedIdentity(DesignTopology topology) => ContentFingerprint.Compute(topology with { Name = "seed" });
    private static bool SameDescriptor(CenteredPrescriptionDescriptor a, CenteredPrescriptionDescriptor b) =>
        new[] { (a.EffectiveFocalLengthMillimeters, b.EffectiveFocalLengthMillimeters), (a.FNumber, b.FNumber),
            (a.MaximumHalfFieldDegrees, b.MaximumHalfFieldDegrees), (a.BackWorkingSpaceMillimeters, b.BackWorkingSpaceMillimeters),
            (a.BackFocalLengthMillimeters, b.BackFocalLengthMillimeters) }
        .All(pair => Math.Abs(pair.Item1 - pair.Item2) <= 1e-12 * Math.Max(1, Math.Abs(pair.Item1)));
    private static double ScaleFor(CenteredPrescriptionDescriptor descriptor, TopologyTargets targets) =>
        Math.Abs(descriptor.EffectiveFocalLengthMillimeters / targets.EffectiveFocalLengthMillimeters - 1) <= targets.FocalLengthRelativeTolerance
            ? 1 : targets.EffectiveFocalLengthMillimeters / descriptor.EffectiveFocalLengthMillimeters;
    private static AutonomousSearchWork Sum(AutonomousSearchWork a, AutonomousSearchWork b) => new()
    {
        FunctionEvaluations = checked(a.FunctionEvaluations + b.FunctionEvaluations),
        GradientEvaluations = checked(a.GradientEvaluations + b.GradientEvaluations),
        ValidationEvaluations = checked(a.ValidationEvaluations + b.ValidationEvaluations),
        MatrixEvaluations = checked(a.MatrixEvaluations + b.MatrixEvaluations),
        DifferenceEvaluations = checked(a.DifferenceEvaluations + b.DifferenceEvaluations),
        JacobianBuilds = checked(a.JacobianBuilds + b.JacobianBuilds),
        TracedRays = checked(a.TracedRays + b.TracedRays),
        GeometryProbes = checked(a.GeometryProbes + b.GeometryProbes),
        Elapsed = a.Elapsed + b.Elapsed
    };
}
