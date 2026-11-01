using System.Collections.Immutable;
using System.Diagnostics;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public sealed record StructureRayFailure(TopologyFieldCoordinate Field, double WavelengthNanometers,
    double PupilX, double PupilY, SequentialFailureDiagnostic Diagnostic);
public sealed record StructureLossReport(string OpticFingerprint, string Backend, string Sampling,
    bool Completed, string StopReason, int AttemptedSamples, int ReachedImage,
    ImmutableDictionary<string, int> FailureCounts, ImmutableArray<StructureRayFailure> Failures,
    long TracedRays, TimeSpan Elapsed, bool EvidenceTruncated);

/// <summary>Bounded observation of an unchanged compiled snapshot; never contributes to qualification.</summary>
public static class StructureSearchDiagnostics
{
    /// <summary>Necessary geometry condition for M2's fixed-aperture edits only.
    /// An infinite axial bundle reaches a front stop and the first refractor before any
    /// bending. M2 never enlarges a retained or newly created face beyond these caps.</summary>
    internal static StructurePrecheck ExternalStopAperturePrecheck(DesignTopology topology)
    {
        var applicable = topology.Conjugate == ObjectConjugateMode.Infinite && !topology.HasTransitionElements
            && topology.CementedInterfaces is null && !topology.Elements.IsDefaultOrEmpty
            && topology.Stop.ZMillimeters <= topology.Reference.ZMillimeters
            && topology.Elements.All(e => e.FrontZMillimeters >= topology.Reference.ZMillimeters)
            && topology.Fields.Any(f => f.Weight > 0 && f.XAngleDegrees == 0 && f.YAngleDegrees == 0);
        var maximum = topology.Elements.IsDefaultOrEmpty ? 0 : topology.Elements.Max(e =>
            Math.Max(e.Front.ClearSemiDiameterMillimeters, e.Back.ClearSemiDiameterMillimeters));
        var clipped = maximum > 0 && !new CircularAperture(maximum).Contains(new Vector3D(topology.Stop.ClearSemiDiameterMillimeters, 0, 0));
        return new("ExternalStopExceedsEveryFixedFrontAperture", applicable && clipped,
            topology.Stop.ClearSemiDiameterMillimeters, maximum,
            (applicable ? "Applicable: " : "Not proven outside scope: ")
            + "infinite axial field; stop at/before fixed reference; every refractor after reference; M2 edits keep fixed aperture caps, "
            + "fixed stop/reference and no pre-stop optical element. At least one required rim ray is clipped before first refraction. "
            + "This is not a general infeasibility claim for internal stops, finite conjugates or adjustable apertures.");
    }

    public static ImmutableArray<StructurePrecheck> Precheck(AutonomousSearchPlan plan, StructureGenerationOptions descriptor)
    {
        var t = plan.Seeds[0]; var count = descriptor.Elements.Length;
        var margin = Math.Max(.05, plan.Constraints.MinimumAirGapMillimeters);
        var glass = Math.Max(descriptor.MinimumThicknessMillimeters, plan.Constraints.MinimumCenterThicknessMillimeters);
        var back = Math.Max(descriptor.MinimumBackFocusMillimeters, plan.Constraints.MinimumBackFocusMillimeters);
        var partitions = descriptor.FeasibleLayouts is { StopAfterElementCandidates.IsEmpty: false } f
            ? f.StopAfterElementCandidates.ToArray() : [descriptor.StopAfterElement];
        var deficits = partitions.Select(n =>
        {
            var front = n == 0 ? 0 : n * (glass + margin)
                - (Math.Min(t.Stop.ZMillimeters - margin, t.Image.ZMillimeters - back) - t.Reference.ZMillimeters);
            var rear = n == count ? 0 : (count - n) * (glass + margin)
                - (t.Image.ZMillimeters - back - t.Stop.ZMillimeters - margin);
            return Math.Max(front, rear);
        }).ToArray();
        return [new("MinimumAxialSpaceDeficitMillimeters", deficits.All(v => v > 0), deficits.Min(), 0,
            "Fixed reference/stop/image; permitted stop partitions only; minimum center thickness and required air/stop/back clearances. Positive deficit for every partition proves no layout fits. Zero is not proof.")];
    }

    public static long EstimateFinalRays(CompiledTopology source, double recoveryFactor)
    {
        if (!double.IsFinite(recoveryFactor) || recoveryFactor < 1 || recoveryFactor > 20) throw new ArgumentOutOfRangeException(nameof(recoveryFactor));
        var fields = TopologyEvaluationPlan.Fields(source, 21).Length;
        var waves = source.Topology.Wavelengths.Count(w => w.Weight > 0);
        var samples = ApertureSampler.GenerateGaussianQuadrature(10, 32).Count + TopologyEvaluationPlan.Checks().Length;
        // Lower-level actual aiming, failed traces and recovery are charged by the shared budget.
        // This is a dispatch estimate, never a guarantee of completed validation.
        return checked((long)Math.Ceiling(fields * (long)waves * (samples + 2) * recoveryFactor));
    }

    public static StructureLossReport Analyze(CompiledTopology source, long maximumRays = 100000,
        TimeSpan? timeLimit = null, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew(); using var deadline = new CancellationTokenSource(timeLimit ?? TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = linked.Token; using var budget = SequentialTraceMeasurement.BeginCpuBudget(maximumRays);
        using var cpu = new SequentialGpuSession(DesignComputeMode.Cpu, token);
        var failures = new List<StructureRayFailure>(); var counts = new Dictionary<string, int>();
        int attempted = 0, reached = 0; var completed = false; var reason = "Completed";
        var optic = source.CreateOptic();
        try
        {
            var pupils = ApertureSampler.GenerateGaussianQuadrature(2, 8).Concat(TopologyEvaluationPlan.Checks(8)).ToArray();
            foreach (var field in TopologyEvaluationPlan.Fields(source, 3))
                foreach (var wave in optic.Wavelengths.Where(w => w.Weight > 0))
                    foreach (var pupil in pupils)
                    {
                        token.ThrowIfCancellationRequested(); attempted++;
                        var ray = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field.X, field.Y, wave.Micrometers,
                            [pupil], aimAtStop: optic.RayAimingEnabled, applyVignettingFactors: false).Rays[0];
                        var result = optic.SequentialRayTracer.DiagnoseFirstFailure(ray, token);
                        counts[result.Failure.ToString()] = counts.GetValueOrDefault(result.Failure.ToString()) + 1;
                        if (result.ReachedImage) reached++;
                        else if (failures.Count < 256) failures.Add(new(field, wave.Nanometers, pupil.X, pupil.Y, result));
                        if (result.Failure is SurfaceTraceFailure.Cancelled or SurfaceTraceFailure.RayBudgetExhausted)
                        { reason = result.Failure.ToString(); break; }
                    }
            completed = reason == "Completed";
        }
        catch (OperationCanceledException) { reason = cancellationToken.IsCancellationRequested ? "Cancelled" : "TimeLimit"; }
        catch (Exception error) when (SequentialTraceBudgetExceededException.IsBudgetException(error)) { reason = "RayBudgetExhausted"; }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { reason = "UnsupportedOrDomain:" + error.Message; }
        return new(source.OpticFingerprint, "CPU/double", "Core/fields3/Gaussian2x8/center-and-rim8/positive-weight-wavelengths",
            completed, reason, attempted, reached, counts.ToImmutableDictionary(), failures.ToImmutableArray(), budget.RayCount,
            timer.Elapsed, attempted - reached > failures.Count);
    }
}
