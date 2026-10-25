using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Materials;
using static OpticalSystemDesign.Core.Differentiation.DifferentialScalar;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>AutoLens-specific training statistics and loss assembly, following optim.py at e58. Physical tracing and dispersion use shared Core.</summary>
internal static class AutoLensReferenceLoss
{
    private const double Epsilon = 1e-12;

    public static DifferentialScalar Image(FixedInputOpticalResult optical, bool centroid)
    {
        var fields = AggregateSpotStatistics(optical, referenceWave: 1, useReferenceCentroid: centroid);
        if (fields.Count == 0 || fields.Any(field => field.RmsRadii.Count != 3))
            throw new ArgumentException("Reference RGB loss requires exactly three active colours.");
        var weights = fields.Select(field => field.ReferenceRmsWeight).ToArray();
        DifferentialScalar distortion = 0;
        double distortionWeights = 0;
        var maximumIdeal = fields.Max(field => field.IdealRadius);
        foreach (var field in fields)
        {
            if (field.IdealRadius > Epsilon)
            {
                var weight = 1 + Math.Pow(field.IdealRadius / Math.Max(maximumIdeal, Epsilon), 2);
                distortion += weight * PositivePart((field.RelativeDistortion - .03) / .03);
                distortionWeights += weight;
            }
        }
        var mean = weights.Average();
        for (var index = 0; index < weights.Length; index++) weights[index] = fields[index].Ring == 0 ? 2 : weights[index] / (mean + Epsilon);
        DifferentialScalar rms = 0;
        var weightSum = weights.Sum();
        for (var index = 0; index < fields.Count; index++)
            for (var wave = 0; wave < 3; wave++)
                rms += weights[index] * fields[index].RmsRadii[wave]
                    / (weightSum + Epsilon) / 3;
        return rms + .1 * distortion / (distortionWeights + Epsilon);
    }

    private sealed record FixedInputSpotStatistics(int Ring, int Arm, double IdealRadius, double ReferenceRmsWeight,
        DifferentialScalar RelativeDistortion, IReadOnlyList<DifferentialScalar> RmsRadii);

    /// <summary>Training statistics for equal-area, equal-weight rays, using the chosen wavelength's detached
    /// centroid or the supplied ideal point as the common RGB reference. Unlike a formal spot analysis this
    /// applies count + epsilon normalization and an epsilon floor to squared RMS, including empty groups.
    /// It neither applies spectral/throughput weights nor changes the formal analysis reference or validity rules.
    /// The caller assembles objective weights and target penalties; these statistics never certify throughput.</summary>
    private static IReadOnlyList<FixedInputSpotStatistics> AggregateSpotStatistics(FixedInputOpticalResult optical,
        int referenceWave, bool useReferenceCentroid, double epsilon = 1e-12)
    {
        if (!double.IsFinite(epsilon) || epsilon <= 0) throw new ArgumentOutOfRangeException(nameof(epsilon));
        var result = new List<FixedInputSpotStatistics>();
        foreach (var group in optical.Fields.GroupBy(field => (field.Input.Ring, field.Input.Arm)))
        {
            var fields = group.OrderBy(field => field.Input.Wave).ToArray();
            if (referenceWave < 0 || referenceWave >= fields.Length
                || fields.Select(field => field.Input.Wave).Where((wave, index) => wave != index).Any())
                throw new ArgumentException("Spot groups require one contiguous ordered set of wavelengths.");
            var reference = fields[referenceWave];
            var valid = reference.Rays.Where(ray => ray.Valid).ToArray();
            DifferentialScalar cx = 0, cy = 0;
            foreach (var ray in valid) { cx += ray.X / (valid.Length + epsilon); cy += ray.Y / (valid.Length + epsilon); }
            var referenceX = useReferenceCentroid ? cx.Value : reference.Input.IdealX;
            var referenceY = useReferenceCentroid ? cy.Value : reference.Input.IdealY;
            var rms = new DifferentialScalar[fields.Length];
            double referenceWeight = 0;
            for (var wave = 0; wave < fields.Length; wave++)
            {
                var transmitted = fields[wave].Rays.Where(ray => ray.Valid).ToArray();
                DifferentialScalar energy = 0;
                foreach (var ray in transmitted)
                    energy += DifferentialScalar.Square(ray.X - referenceX) + DifferentialScalar.Square(ray.Y - referenceY);
                var mse = energy / (transmitted.Length + epsilon);
                if (wave == referenceWave) referenceWeight = Math.Sqrt(mse.Value);
                rms[wave] = mse.Value < epsilon ? Math.Sqrt(epsilon) : DifferentialScalar.Sqrt(mse);
            }
            var ideal = Math.Sqrt(reference.Input.IdealX * reference.Input.IdealX + reference.Input.IdealY * reference.Input.IdealY);
            var distortion = DifferentialScalar.Sqrt(DifferentialScalar.Square(cx - reference.Input.IdealX)
                + DifferentialScalar.Square(cy - reference.Input.IdealY)) / Math.Max(ideal, epsilon);
            result.Add(new(group.Key.Ring, group.Key.Arm, ideal, referenceWeight, distortion, rms));
        }
        return result;
    }
    public static DifferentialScalar RayRegularization(FixedInputOpticalResult cra, FixedInputOpticalResult bend, double craCosine)
    {
        var craRays = cra.Fields.SelectMany(field => field.Rays).Where(ray => ray.Valid).ToArray();
        var bendRays = bend.Fields.SelectMany(field => field.Rays).Where(ray => ray.Valid && ray.BendPenalty.Value > Epsilon).ToArray();
        DifferentialScalar result = 0;
        foreach (var ray in craRays) result += PositivePart((craCosine - ray.DirectionZ) / Math.Max(1 - craCosine, Epsilon)) / (craRays.Length + Epsilon);
        foreach (var ray in bendRays) result += ray.BendPenalty / (bendRays.Length + Epsilon);
        return result;
    }

    public static DifferentialScalar Geometry(Optic optic, IReadOnlyList<OpticalDesignParameter> parameters,
        bool compact, bool optimizeMaterials, IReadOnlySet<int>? referenceRows = null)
    {
        DifferentialScalar result = 0;
        var last = optic.SurfaceGroup.Items.Count - 2;
        var airMin = compact ? .05 : .1;
        var airMax = compact ? 5 : 20;
        for (var index = 1; index <= last; index++)
        {
            if (referenceRows?.Contains(index) == true) continue;
            var surface = optic.SurfaceGroup.Items[index];
            if (index < last)
            {
                var next = index + 1;
                while (referenceRows?.Contains(next) == true) next++;
                var air = surface.MaterialAfter is AirMaterial;
                var minCenter = air ? airMin : compact ? .4 : 2;
                var minEdge = air ? airMin : compact ? .4 : 1;
                var maximum = air ? airMax : compact ? 2 : 20;
                result += Bound(DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, index, next, 0, relaxedDomain: true), minCenter, maximum);
                for (var sample = 0; sample < 64; sample++)
                    result += Bound(DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, index, next,
                        surface.SemiDiameter * (.5 + .5 * sample / 63), relaxedDomain: true), minEdge, maximum) / 64;
            }
            if (!surface.IsStop)
            {
                var sagMax = compact ? .2 : .5;
                var slopeMax = Math.Tan((compact ? 30 : 45) * Math.PI / 180);
                for (var sample = 0; sample < 64; sample++)
                {
                    var radius = surface.SemiDiameter * sample / 63;
                    result += PositivePart((Abs(DifferentiableOpticalMetrics.SurfaceSag(optic, parameters, index, radius, relaxedDomain: true, maskOutsideProfile: true)) / surface.SemiDiameter / 2 - sagMax) / sagMax) / 64;
                    result += PositivePart((Abs(DifferentiableOpticalMetrics.SurfaceSlope(optic, parameters, index, radius, relaxedDomain: true, maskOutsideProfile: true)) - slopeMax) / slopeMax) / 64;
                }
                if (optimizeMaterials && surface.MaterialAfter is not AirMaterial)
                {
                    var n = parameters.Select((parameter, slot) => (parameter, slot)).First(item => item.parameter.SurfaceIndex == index && item.parameter.Kind == OpticalDesignParameterKind.MaterialIndex);
                    var v = parameters.Select((parameter, slot) => (parameter, slot)).First(item => item.parameter.SurfaceIndex == index && item.parameter.Kind == OpticalDesignParameterKind.MaterialAbbe);
                    var coordinates = OpticalDesignParameters.RelaxedGlassCoordinates(surface.MaterialAfter);
                    result += Bound(Variable(coordinates.Nd, parameters.Count, n.slot, n.parameter.Scale), 1.5, 1.9) / .4;
                    result += Bound(Variable(coordinates.Vd, parameters.Count, v.slot, v.parameter.Scale), 30, 70) / 40;
                }
            }
        }
        for (var sample = 0; sample < 64; sample++)
            result += Bound(DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, last, last + 1,
                optic.SurfaceGroup.Items[last].SemiDiameter * sample / 63, relaxedDomain: true, maskOutsideProfile: true), compact ? 1 : 10, compact ? 5 : 100) / 64;
        result += Bound(DifferentiableOpticalMetrics.AxialSeparation(optic, parameters, 1, last + 1, 0, relaxedDomain: true), compact ? 5 : 30, compact ? 15 : 300);
        return result;
    }

    private static DifferentialScalar Bound(DifferentialScalar value, double minimum, double maximum) =>
        PositivePart(minimum - value) + PositivePart(value - maximum);
}
