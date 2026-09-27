using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.Services;

public sealed record RayEnvelopeSurfaceSize(int SurfaceIndex, double SampledRadiusMillimeters, double SemiDiameterMillimeters)
{
    public double MechanicalSemiDiameterMillimeters { get; init; } = SemiDiameterMillimeters;
}

/// <summary>A sampled envelope, not a guarantee between samples or a throughput result.</summary>
public sealed record RayEnvelopeSizingResult(int AttemptedRays, int CompletedRays,
    bool Applied, IReadOnlyList<RayEnvelopeSurfaceSize> Surfaces);

public static partial class AutomaticSemiDiameterSolver
{
    /// <summary>
    /// Resize explicitly selected surface groups from real rays. Each group shares one
    /// clear/mechanical radius (e.g. the two faces of a lens). Their old circular aperture
    /// constraints are removed on a private tracing snapshot; all other apertures remain.
    /// Apply atomically only when every requested ray reaches the image. Invalid propagation
    /// is never replaced by an estimated radius. Honors the optic's ray-aiming setting.
    /// With preserveStopAperture, the existing stop remains a clipping aperture and its
    /// clear radius stays fixed; only its mechanical lens-body radius follows the group.
    /// The caller must validate the resulting geometry.
    /// </summary>
    public static RayEnvelopeSizingResult UpdateFromRayEnvelope(Optic optic,
        IReadOnlyList<IReadOnlyList<int>> surfaceGroups,
        IReadOnlyList<(double X, double Y)> normalizedFields,
        IReadOnlyList<PupilSample> pupilSamples, double marginFactor = 1,
        int wavelengthNumber = 0, CancellationToken cancellationToken = default, bool preserveStopAperture = false)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(surfaceGroups);
        ArgumentNullException.ThrowIfNull(normalizedFields);
        ArgumentNullException.ThrowIfNull(pupilSamples);
        cancellationToken.ThrowIfCancellationRequested();
        using var cancellationScope = ComputationCancellation.Push(
            cancellationToken.CanBeCanceled ? cancellationToken : ComputationCancellation.Current);
        if (surfaceGroups.Count == 0 || surfaceGroups.Any(group => group is null || group.Count == 0))
            throw new ArgumentException("Nonempty surface groups are required.", nameof(surfaceGroups));
        var indices = surfaceGroups.SelectMany(group => group).ToArray();
        var count = optic.SurfaceGroup.Items.Count;
        if (indices.Distinct().Count() != indices.Length || indices.Any(index => index <= 0 || index >= count - 1))
            throw new ArgumentException("Surface groups must contain distinct physical surface indices.", nameof(surfaceGroups));
        if (!double.IsFinite(marginFactor) || marginFactor < 1)
            throw new ArgumentOutOfRangeException(nameof(marginFactor));
        if (normalizedFields.Count == 0 || normalizedFields.Any(field => !double.IsFinite(field.X)
            || !double.IsFinite(field.Y) || Math.Abs(field.X) > 1 || Math.Abs(field.Y) > 1))
            throw new ArgumentException("Finite normalized field samples are required.", nameof(normalizedFields));
        if (pupilSamples.Count == 0 || pupilSamples.Any(sample => !double.IsFinite(sample.X) || !double.IsFinite(sample.Y)
            || sample.X * sample.X + sample.Y * sample.Y > 1 + 1e-12 || !double.IsFinite(sample.Weight) || sample.Weight <= 0))
            throw new ArgumentException("Finite normalized positive-weight pupil samples are required.", nameof(pupilSamples));
        if (wavelengthNumber < 0 || wavelengthNumber > optic.Wavelengths.Count)
            throw new ArgumentOutOfRangeException(nameof(wavelengthNumber));
        var wavelengths = AnalysisTrace.SelectWavelengths(optic, wavelengthNumber).Where(wave => wave.Weight > 0).ToArray();
        var requested = (long)normalizedFields.Count * pupilSamples.Count * wavelengths.Length;
        if (requested is <= 0 or > SequentialTraceLimits.MaximumRayCount)
            throw new ArgumentException("The envelope requires 1 to 1000000 rays.");
        foreach (var index in indices)
            if (optic.SurfaceGroup.Items[index].PhysicalAperture is { } aperture && aperture is not CircularAperture)
                throw new ArgumentException("Only explicitly selected circular clear apertures can be resized.", nameof(surfaceGroups));
        var fixedStops = preserveStopAperture
            ? indices.Where(index => optic.SurfaceGroup.Items[index].IsStop).ToHashSet() : [];
        foreach (var index in fixedStops)
            if (optic.SurfaceGroup.Items[index].PhysicalAperture is not CircularAperture aperture
                || aperture.Radius != optic.SurfaceGroup.Items[index].SemiDiameter)
                throw new ArgumentException("A preserved stop needs a circular aperture matching its clear semi-diameter.", nameof(preserveStopAperture));

        // FromSnapshot creates an independent optic without the source's shared trace cache.
        var working = Optic.FromSnapshot(optic.ToSnapshot());
        foreach (var index in indices)
        {
            if (fixedStops.Contains(index)) continue;
            working.SurfaceGroup.Items[index].SemiDiameterDefinesPhysicalAperture = false;
            working.SurfaceGroup.Items[index].PhysicalAperture = null;
        }
        var maxima = new double[count];
        var completed = 0;
        var retained = indices.Append(count - 1).ToArray();
        foreach (var field in normalizedFields)
            foreach (var wave in wavelengths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bundle = working.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                    field.X, field.Y, wave.Micrometers, pupilSamples,
                    aimAtStop: working.RayAimingEnabled, applyVignettingFactors: false);
                using var trace = working.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(retained));
                for (var ray = 0; ray < trace.RayCount; ray++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var valid = trace.TryGetSample(ray, count - 1, out var image) && !image.Vignetted
                        && image.Intensity > 0 && double.IsFinite(image.Position.X) && double.IsFinite(image.Position.Y);
                    foreach (var index in indices)
                    {
                        if (!trace.TryGetSample(ray, index, out var sample) || sample.Vignetted || sample.Intensity <= 0)
                        { valid = false; continue; }
                        var local = working.SurfaceGroup.Items[index].CoordinateSystem.ToLocalPoint(sample.Position);
                        var radius = double.Hypot(local.X, local.Y);
                        if (!double.IsFinite(radius)) { valid = false; continue; }
                        maxima[index] = Math.Max(maxima[index], radius);
                    }
                    if (valid) completed++;
                }
            }
        var sizes = surfaceGroups.SelectMany(group =>
        {
            // OpticalSurface's minimum supported clear semi-diameter is 0.1 mm.
            var radius = Math.Max(.1, group.Max(index => maxima[index]) * marginFactor);
            radius = Math.Max(radius, group.Where(fixedStops.Contains)
                .Select(index => optic.SurfaceGroup.Items[index].SemiDiameter).DefaultIfEmpty(0).Max());
            return group.Select(index => new RayEnvelopeSurfaceSize(index, maxima[index],
                fixedStops.Contains(index) ? optic.SurfaceGroup.Items[index].SemiDiameter : radius)
            { MechanicalSemiDiameterMillimeters = radius });
        }).ToArray();
        var complete = completed == requested && sizes.All(size => double.IsFinite(size.SemiDiameterMillimeters));
        cancellationToken.ThrowIfCancellationRequested();
        if (complete)
        {
            optic.InvalidateRayTraceCache();
            foreach (var size in sizes)
            {
                var surface = optic.SurfaceGroup.Items[size.SurfaceIndex];
                surface.MechanicalSemiDiameter = size.MechanicalSemiDiameterMillimeters;
                if (!fixedStops.Contains(size.SurfaceIndex))
                {
                    surface.SemiDiameter = size.SemiDiameterMillimeters;
                    surface.SemiDiameterDefinesPhysicalAperture = true;
                    surface.PhysicalAperture = new CircularAperture(size.SemiDiameterMillimeters);
                }
            }
        }
        return new((int)requested, completed, complete, sizes);
    }
}
