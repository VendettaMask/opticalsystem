namespace OpticalSystemDesign.Core.Services;

/// <summary>Air-space Delano construction from a pair of paraxial ray-height diagrams.
/// A proposal model only; finite-thickness physical realizations require normal Core validation.</summary>
public sealed record DelanoConstruction(double[] DistancesMillimeters, double[] PowersPerMillimeter,
    double[] RequiredSemiDiametersMillimeters);

public static class DelanoDiagram
{
    /// <summary>Symmetric air/glass/air thick-singlet seed: C of the actual paraxial
    /// matrix is -power. This is an initialization, not a manufacturing/spot acceptance.</summary>
    public static double SymmetricSingletCurvature(double powerPerMillimeter, double refractiveIndex, double thicknessMillimeters)
    {
        if (!double.IsFinite(powerPerMillimeter) || !double.IsFinite(refractiveIndex) || refractiveIndex <= 1
            || !double.IsFinite(thicknessMillimeters) || thicknessMillimeters <= 0)
            throw new ArgumentException("Finite singlet power, glass index > 1 and positive thickness are required.");
        var discriminant = 1 - thicknessMillimeters * powerPerMillimeter / refractiveIndex;
        if (!double.IsFinite(discriminant) || discriminant < 0) throw new ArithmeticException("No finite real symmetric thick-singlet realization exists.");
        // Stable root of power=(n-1)*(2*c-(n-1)*t*c*c/n).
        var curvature = powerPerMillimeter / ((refractiveIndex - 1) * (1 + Math.Sqrt(discriminant)));
        return double.IsFinite(curvature) ? curvature : throw new ArithmeticException("The thick-singlet curvature is not representable.");
    }

    /// <summary>The final node is the image. The marginal input slope is zero, the
    /// chief input slope is invariant / marginal[0]. Sign convention: u' = u - power*y.</summary>
    public static DelanoConstruction Construct(IReadOnlyList<double> marginal, IReadOnlyList<double> chief, double invariant)
    {
        ArgumentNullException.ThrowIfNull(marginal); ArgumentNullException.ThrowIfNull(chief);
        if (marginal.Count is < 3 or > 17 || marginal.Count != chief.Count
            || marginal.Concat(chief).Any(value => !double.IsFinite(value))
            || !double.IsFinite(invariant) || invariant == 0 || marginal[0] == 0)
            throw new ArgumentException("A finite, nondegenerate two-ray Delano diagram with 2–16 elements is required.");
        var count = marginal.Count - 1;
        var distances = new double[count]; var powers = new double[count]; var radii = new double[count];
        double marginalSlope = 0, chiefSlope = invariant / marginal[0];
        for (var i = 0; i < count; i++)
        {
            var distance = (marginal[i] * chief[i + 1] - chief[i] * marginal[i + 1]) / invariant;
            if (!double.IsFinite(distance) || distance <= 1e-9)
                throw new ArithmeticException("The diagram does not define positive finite forward separations.");
            var nextMarginalSlope = (marginal[i + 1] - marginal[i]) / distance;
            var nextChiefSlope = (chief[i + 1] - chief[i]) / distance;
            var power = Math.Abs(marginal[i]) >= Math.Abs(chief[i])
                ? (marginalSlope - nextMarginalSlope) / marginal[i]
                : (chiefSlope - nextChiefSlope) / chief[i];
            var residual = chiefSlope - power * chief[i] - nextChiefSlope;
            var marginalResidual = marginalSlope - power * marginal[i] - nextMarginalSlope;
            if (!double.IsFinite(power) || Math.Abs(residual) > 1e-9 * (1 + Math.Abs(chiefSlope) + Math.Abs(nextChiefSlope))
                || Math.Abs(marginalResidual) > 1e-9 * (1 + Math.Abs(marginalSlope) + Math.Abs(nextMarginalSlope)))
                throw new ArithmeticException("The diagram violates the common two-ray optical invariant.");
            distances[i] = distance; powers[i] = power; radii[i] = Math.Abs(marginal[i]) + Math.Abs(chief[i]);
            marginalSlope = nextMarginalSlope; chiefSlope = nextChiefSlope;
        }
        return new(distances, powers, radii);
    }
}
