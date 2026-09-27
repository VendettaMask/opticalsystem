using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public sealed record PhysicalStopCalibrationResult(int SurfaceIndex, double ClearSemiDiameterMillimeters,
    double EntrancePupilDiameterMillimeters);

/// <summary>Resolve a circular physical stop from the system's declared paraxial pupil.</summary>
public static class PhysicalStopCalibration
{
    /// <summary>
    /// Set the stop's clear aperture from the primary-wavelength axial paraxial marginal ray.
    /// Preserve its mechanical extent and the declared system aperture. This establishes
    /// first-order aperture consistency, not real-ray throughput or an aberrated-pupil proof.
    /// All validation precedes mutation; unsupported or singular pupils fail explicitly.
    /// </summary>
    public static PhysicalStopCalibrationResult Apply(Optic optic)
    {
        ArgumentNullException.ThrowIfNull(optic);
        var stops = optic.SurfaceGroup.Items.Select((surface, index) => (surface, index))
            .Where(item => item.surface.IsStop).ToArray();
        if (stops.Length != 1 || stops[0].index <= 0 || stops[0].index >= optic.SurfaceGroup.Items.Count - 1)
            throw new InvalidOperationException("A single physical stop surface is required.");
        if (optic.Aperture.Kind is not (ApertureKind.EntrancePupilDiameter or ApertureKind.FNumber or ApertureKind.FloatByStopSize))
            throw new NotSupportedException("Physical stop calibration requires entrance-pupil diameter, F-number or floating stop size.");
        var (stop, index) = stops[0];
        if (stop.PhysicalAperture is not (null or CircularAperture))
            throw new NotSupportedException("Physical stop calibration supports circular apertures only.");
        var primary = optic.Wavelengths.FirstOrDefault(wave => wave.IsPrimary)
            ?? throw new InvalidOperationException("A primary wavelength is required.");
        var diameter = optic.Paraxial.EstimateEntrancePupilDiameter();
        var trace = optic.Paraxial.TraceNormalizedPupil(0, [1.0], primary.Micrometers);
        var radius = index < trace.Heights.Count ? Math.Abs(trace.Heights[index][0]) : double.NaN;
        if (!double.IsFinite(diameter) || diameter <= 0 || !double.IsFinite(radius) || radius < .1)
            throw new InvalidOperationException("The pupil has no finite stop radius within the supported clear-aperture range.");
        var mechanical = Math.Max(stop.MechanicalSemiDiameter, radius);
        optic.InvalidateRayTraceCache();
        stop.SemiDiameter = radius;
        stop.MechanicalSemiDiameter = mechanical;
        stop.SemiDiameterFixed = true;
        stop.SemiDiameterDefinesPhysicalAperture = true;
        stop.PhysicalAperture = new CircularAperture(radius);
        return new(index, radius, diameter);
    }
}
