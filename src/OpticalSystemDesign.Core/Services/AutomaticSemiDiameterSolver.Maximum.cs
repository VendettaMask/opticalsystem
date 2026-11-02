using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Core.Services;

public static partial class AutomaticSemiDiameterSolver
{
    /// <summary>Resolve Maximum from the physical pupil footprints of all configurations.
    /// Only apertures owned by this derived solve are suppressed on private probes;
    /// every other physical aperture still limits propagation. Commit after all probes.</summary>
    public static void UpdateMaximum(IReadOnlyList<Optic> configurations)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        if (!configurations.Any(o => o.SurfaceGroup.Items.Any(s => s.SemiDiameterMaximum))) return;
        var count = configurations[0].SurfaceGroup.Items.Count;
        if (configurations.Any(o => o.SurfaceGroup.Items.Count != count))
            throw new NotSupportedException("Maximum semi-diameter requires matching physical surfaces across configurations.");
        if (configurations.Any(o => o.SurfaceGroup.Items.Any(s => s.MaterialAfter is UnresolvedMaterial)))
            throw new InvalidOperationException("Maximum semi-diameter requires resolved optical materials.");
        long work = 0;
        foreach (var optic in configurations)
            work = checked(work + (long)Math.Max(1, optic.Fields.Count) * Math.Max(1, optic.Wavelengths.Count) * PupilSamples.Length);
        if (work > AnalysisResourceLimits.MaximumAnalysisRayWork)
            throw new ArgumentOutOfRangeException(nameof(configurations), "Maximum semi-diameter exceeds the aggregate ray-work limit.");
        var targets = Enumerable.Range(0, count).Where(i => configurations.Any(o => o.SurfaceGroup.Items[i].SemiDiameterMaximum)).ToArray();
        var maxima = new double[count];
        foreach (var source in configurations)
        {
            var probe = source.Clone();
            foreach (var surface in probe.SurfaceGroup.Items)
                if (surface.SemiDiameterMaximum && surface.SemiDiameterDefinesPhysicalAperture)
                    surface.PhysicalAperture = null;
            var waves = probe.Wavelengths.Count == 0 ? new[] { .5875618 } : probe.Wavelengths.Select(w => w.Micrometers).ToArray();
            foreach (var field in NormalizedFields(probe)) foreach (var wave in waves) foreach (var pupil in PupilSamples)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                var bundle = probe.SequentialRayTracer.RayGenerator.GenerateGeneric(
                    field.X, field.Y, pupil.X, pupil.Y, wave, probe.RayAimingEnabled);
                var history = probe.SequentialRayTracer.Trace(bundle).RayHistories.Single();
                foreach (var sample in history)
                {
                    var index = sample.SurfaceNumber;
                    if (!targets.Contains(index)) continue;
                    var point = probe.SurfaceGroup.Items[index].CoordinateSystem.ToLocalPoint(sample.Position);
                    var radius = double.Hypot(point.X, point.Y);
                    if (double.IsFinite(radius)) maxima[index] = Math.Max(maxima[index], radius);
                }
            }
        }
        if (targets.Any(i => maxima[i] <= 0))
            throw new InvalidOperationException("Maximum semi-diameter has no traceable pupil footprint.");
        foreach (var optic in configurations) foreach (var index in targets)
        {
            var surface = optic.SurfaceGroup.Items[index];
            if (!surface.SemiDiameterMaximum) continue;
            surface.SemiDiameter = maxima[index];
            if (surface.MechanicalSemiDiameterSolveCode == 0)
                surface.MechanicalSemiDiameter = surface.SemiDiameter + surface.ChipZone;
        }
        foreach (var optic in configurations) optic.Pickups.ApplyAll();
    }
}
