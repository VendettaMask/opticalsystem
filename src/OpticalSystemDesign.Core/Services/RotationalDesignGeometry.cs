using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Core.Services;

/// <summary>Shared geometric operations for rotational design; no quality estimates or acceptance fallbacks.</summary>
public static class RotationalDesignGeometry
{
    public static double FieldEnvelopeRadius(double pupilRadius, double distanceFromStop, double halfFieldRadians) =>
        pupilRadius + Math.Abs(distanceFromStop) * Math.Tan(halfFieldRadians);

    public static double ImageRadius(double focalLength, double halfFieldRadians) => Math.Round(focalLength * Math.Tan(halfFieldRadians), 2);
    public static double HalfFieldRadians(double imageRadius, double focalLength) => Math.Atan(imageRadius / focalLength);
    public static double FocalLengthForField(double imageRadius, double halfFieldRadians) => imageRadius / Math.Tan(halfFieldRadians);
    public static double VertexSeparation(Optic optic, int first, int second) =>
        optic.SurfaceGroup.Items[second].CoordinateSystem.Origin.Z - optic.SurfaceGroup.Items[first].CoordinateSystem.Origin.Z;

    public static void SetRadius(Optic optic, int index, double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0) throw new ArithmeticException("A physical clear aperture must have positive finite radius.");
        var surface = optic.SurfaceGroup.Items[index];
        surface.SemiDiameterFixed = true;
        surface.SemiDiameter = radius;
        surface.MechanicalSemiDiameter = radius;
        surface.PhysicalAperture = new CircularAperture(radius);
        optic.InvalidateRayTraceCache();
    }

    public static void AnchorFrontStop(Optic optic, double frontGap = .1)
    {
        if (!optic.SurfaceGroup.Items[1].IsStop) return;
        // The stop is fixed at the first physical reference. Shift all downstream
        // surfaces including the image by changing this gap, retaining internal spacings.
        optic.SurfaceGroup.Items[1].Thickness = frontGap;
        optic.SurfaceGroup.Renumber();
        optic.InvalidateRayTraceCache();
    }

    public static double MaximumProfileRadius(Optic optic, int index)
    {
        var (curvature, conic, _) = OpticalDesignParameters.Profile(optic.SurfaceGroup.Items[index].Geometry);
        return conic > -1 ? Math.Sqrt(1 / (1 + conic) / Math.Max(curvature * curvature, 1e-12)) - .001 : 10000;
    }

    /// <summary>Trace-based pruning with margin clamp and the upstream two-neighbour 64 by 64 edge-clearance cap.</summary>
    public static void PruneClearApertures(Optic optic, double halfFieldRadians, double focalLength, ulong seed,
        CancellationToken cancellationToken = default, IReadOnlySet<int>? referenceRows = null, int rings = 16, int raysPerField = 1024)
    {
        var count = optic.SurfaceGroup.Items.Count;
        var optical = Enumerable.Range(1, count - 2).Where(index => !optic.SurfaceGroup.Items[index].IsStop && referenceRows?.Contains(index) != true).ToArray();
        foreach (var index in optical) SetRadius(optic, index, MaximumProfileRadius(optic, index));
        if (referenceRows is not null) foreach (var index in referenceRows) SetRadius(optic, index, optic.SurfaceGroup.Items[index - 1].SemiDiameter);
        var rays = FixedInputOpticalMetrics.SampleRingArm(optic, halfFieldRadians, focalLength, rings, 1, raysPerField, 1,
            seed, moreOffAxis: false, meridional: true, primaryOnly: true);
        var heights = new double[count];
        foreach (var field in rays.Fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var trace = optic.SequentialRayTracer.Trace(new RealRayBundle(field.Rays), TraceRequest.FullHistory());
            for (var index = 1; index + 1 < count; index++)
                foreach (var sample in trace.GetSurfaceSamples(index))
                    if (sample is { } point && double.IsFinite(point.Position.X) && double.IsFinite(point.Position.Y))
                        heights[index] = Math.Max(heights[index], Math.Sqrt(point.Position.X * point.Position.X + point.Position.Y * point.Position.Y));
        }
        var proposed = optic.SurfaceGroup.Items.Select(surface => surface.SemiDiameter).ToArray();
        foreach (var index in optical)
        {
            var radius = heights[index] > 0 ? heights[index] : proposed[index];
            proposed[index] = radius + Math.Clamp(radius * .05, .1, 2);
        }
        foreach (var index in optical)
        {
            var previous = index - 1;
            while (referenceRows?.Contains(previous) == true) previous--;
            var next = index + 1;
            while (referenceRows?.Contains(next) == true) next++;
            if (previous > 0) Cap(index, previous, index);
            if (next + 1 < count) Cap(index, index, next);
        }
        foreach (var index in optical) SetRadius(optic, index, Math.Min(proposed[index], MaximumProfileRadius(optic, index)));
        if (referenceRows is not null) foreach (var index in referenceRows) SetRadius(optic, index, optic.SurfaceGroup.Items[index - 1].SemiDiameter);

        void Cap(int cap, int previous, int next)
        {
            if (optic.SurfaceGroup.Items[previous].IsStop || optic.SurfaceGroup.Items[next].IsStop) return;
            var other = cap == previous ? next : previous;
            var radius = proposed[cap];
            if (Clear(Math.Min(radius, proposed[other]))) return;
            var safe = 0.0;
            for (var candidate = 1; candidate <= 64; candidate++)
                if (Clear(Math.Min(radius * candidate / 64, proposed[other]))) safe = radius * candidate / 64;
            var required = Math.Max(heights[cap], .1);
            proposed[cap] = Math.Min(radius, safe >= required ? Math.Max(safe, .1) : required);

            bool Clear(double r)
            {
                var a = optic.SurfaceGroup.Items[previous];
                var b = optic.SurfaceGroup.Items[next];
                for (var point = 0; point < 64; point++)
                {
                    var radial = r * (.5 + .5 * point / 63);
                    var gap = b.CoordinateSystem.Origin.Z - a.CoordinateSystem.Origin.Z
                        + DifferentiableOpticalMetrics.SurfaceSag(optic, [], next, radial, relaxedDomain: true).Value
                        - DifferentiableOpticalMetrics.SurfaceSag(optic, [], previous, radial, relaxedDomain: true).Value;
                    if (!double.IsFinite(gap) || gap < .1) return false;
                }
                return true;
            }
        }
    }

    /// <summary>Upstream post-computation's 64-angle, 256-ray finite-object sensor-edge field selection.</summary>
    public static double SensorEdgeHalfField(Optic optic, double imageRadius, double focalLength, ulong seed,
        CancellationToken cancellationToken = default, int rings = 64, int raysPerField = 256)
    {
        var effective = HalfFieldRadians(imageRadius, focalLength);
        var minimum = effective * .5;
        var maximum = Math.Min(effective * 1.5, 89 * Math.PI / 180);
        var inputs = FixedInputOpticalMetrics.SampleRingArm(optic, maximum, focalLength, rings, 1, raysPerField, 1, seed,
            moreOffAxis: false, meridional: true, primaryOnly: true, minimumHalfFieldRadians: minimum);
        var best = effective;
        var bestError = double.PositiveInfinity;
        foreach (var field in inputs.Fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var trace = optic.SequentialRayTracer.Trace(new RealRayBundle(field.Rays), TraceRequest.FinalOnly(false));
            var valid = trace.GetSurfaceSamples(optic.SurfaceGroup.Items.Count - 1)
                .Where(sample => sample is { Vignetted: false, Intensity: > 0 }).Select(sample => sample!.Value).ToArray();
            if (valid.Length <= 10) continue;
            var error = Math.Abs(Math.Abs(valid.Average(sample => sample.Position.Y)) - imageRadius);
            if (error >= bestError) continue;
            bestError = error;
            best = minimum + field.Ring * (maximum - minimum) / (rings - 1);
        }
        return best;
    }

}
