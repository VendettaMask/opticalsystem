using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class FocalImageDomain
{
    public const string OutsideFftNearFocus = "OutsideFftNearFocus";
    public const string FftNearFocusEvidenceKey = "FftNearFocusEvidence";

    private const double MinimumTransverseMissWaves = 10_000;
    private const double MinimumSphericalCorrectionWaves = 100;
    private const double InnerProbeRadius = .6;
    private const double OuterProbeRadius = .85;
    private static readonly double Diagonal = Math.Sqrt(.5);
    private static readonly (double X, double Y)[] Diameters = [(1, 0), (Diagonal, Diagonal), (0, 1), (-Diagonal, Diagonal)];

    internal static AnalysisData? CheckFftNearFocus(string name, Optic optic, (double Hx, double Hy) field, Wavelength wave)
    {
        // This is a sufficient exclusion for severe departures. Modest misses,
        // incomplete pupils and unmeasured configurations remain on the normal
        // path, where the phase sampling and result checks still apply.
        if (optic.ImageSpaceAfocal || field != (0d, 0d) || optic.SurfaceGroup.Items.Count < 3) return null;
        var image = optic.SurfaceGroup.Items[^1];
        if (image.Geometry is not (PlaneGeometry or StandardGeometry { Radius: 0 })
            || image.MaterialAfter.PropagationModel is not HomogeneousPropagationModel) return null;

        FftNearFocusEvidence? evidence;
        try { evidence = FindWitness(optic, wave); }
        catch (Exception error) when (error is InvalidOperationException or ArithmeticException or NotSupportedException)
        { return null; }
        if (evidence is null) return null;

        const string reason = "The requested image plane is outside the near-focus domain of focal FFT diffraction. Transmitted pupil rays miss the chief by many wavelengths and require large spherical-reference corrections.";
        var result = NotApplicable(name, reason, OutsideFftNearFocus);
        return result with { Values = new Dictionary<string, object>(result.Values) { [FftNearFocusEvidenceKey] = evidence } };
    }

    private static FftNearFocusEvidence? FindWitness(Optic optic, Wavelength wave)
    {
        var nodes = new List<PupilSample> { new(0, 0, 1) };
        foreach (var (x, y) in Diameters)
        {
            nodes.Add(new(InnerProbeRadius * x, InnerProbeRadius * y, 1));
            nodes.Add(new(-InnerProbeRadius * x, -InnerProbeRadius * y, 1));
            nodes.Add(new(OuterProbeRadius * x, OuterProbeRadius * y, 1));
            nodes.Add(new(-OuterProbeRadius * x, -OuterProbeRadius * y, 1));
        }
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0,
            wave.Micrometers, nodes, optic.RayAimingEnabled);
        var rays = optic.SequentialRayTracer.TraceFinalSamples(bundle);
        if (rays[0] is not { Vignetted: false, Intensity: > 0 } chief) return null;
        var image = optic.SurfaceGroup.Items[^1];
        var normal = chief.Direction / chief.Direction.Length;
        // Use a chief-normal tangent plane. Leave oblique image-plane cases to
        // their existing paths rather than mixing image tilt with defocus.
        if (!(Math.Abs(image.CoordinateSystem.ToLocalDirection(normal).Z) >= .999)) return null;
        var index = image.MaterialAfter.RefractiveIndex(wave.Nanometers);
        var wavelength = wave.Micrometers * .001 / index;
        if (!(wavelength > 0) || !double.IsFinite(wavelength)) return null;

        var misses = rays.Select(Miss).ToArray();
        if (!Enumerable.Range(0, Diameters.Length).Any(i => HasLargeMiss(i))) return null;
        var sphere = WavefrontEngine.CreateChiefRayReferenceSphere(optic, (0, 0), wave, optic.RayAimingEnabled);
        if (!(sphere.Radius > 0) || !double.IsFinite(sphere.Radius)) return null;

        for (var i = 0; i < Diameters.Length; i++)
        {
            if (!HasLargeMiss(i)) continue;
            var first = 4 * i + 1;
            var inner = Math.Min(ShellCorrection(rays[first]), ShellCorrection(rays[first + 1]));
            var outer = Math.Min(ShellCorrection(rays[first + 2]), ShellCorrection(rays[first + 3]));
            // Require four actual transmitted witnesses on the same diameter,
            // at two interior radii. The spherical sag is nonnegative and zero
            // for the chief; paired samples distinguish it from simple tilt.
            if (!(inner >= MinimumSphericalCorrectionWaves && outer >= MinimumSphericalCorrectionWaves)) continue;
            var (x, y) = Diameters[i];
            return new(wave.Micrometers, sphere.Radius, x, y,
                Math.Min(misses[first], misses[first + 1]), Math.Min(misses[first + 2], misses[first + 3]), inner, outer);
        }
        return null;

        bool HasLargeMiss(int diameter)
        {
            var first = 4 * diameter + 1;
            return Math.Min(Math.Min(misses[first], misses[first + 1]), Math.Min(misses[first + 2], misses[first + 3]))
                >= MinimumTransverseMissWaves;
        }

        double Miss(RayTraceSample? ray)
        {
            if (ray is not { Vignetted: false, Intensity: > 0 } || Dot(ray.Direction, normal) <= 0) return 0;
            var offset = ray.Position - chief.Position;
            return (offset - normal * Dot(offset, normal)).Length / wavelength;
        }

        double ShellCorrection(RayTraceSample? ray)
        {
            var direction = ray!.Direction / ray.Direction.Length;
            var offset = ray.Position - chief.Position;
            var impact = (offset - direction * Dot(offset, direction)).Length;
            var ratio = impact / sphere.Radius;
            // Exact R - sqrt(R² - impact²), written without cancellation.
            return ratio < 1 ? impact * ratio / (1 + Math.Sqrt(1 - ratio * ratio)) / wavelength : 0;
        }
    }

    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}

public sealed record FftNearFocusEvidence(double VacuumWavelengthMicrometers, double ReferenceRadiusMillimeters,
    double PupilDiameterX, double PupilDiameterY, double InnerMinimumTransverseMissWaves,
    double OuterMinimumTransverseMissWaves, double InnerMinimumSphericalCorrectionWaves, double OuterMinimumSphericalCorrectionWaves);
