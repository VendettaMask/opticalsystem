namespace OpticalSystemDesign.Core.Backend;

// Scalar and SIMD roots share identical explicit-sag branch and tolerance checks.
internal static class StandardIntersectionValidation
{
    // Called only after both ordinary roots fail the unchanged explicit-sag
    // validation. Preserve every already accepted ordinary intersection.
    internal static (double First, double Second) StableQuadraticRoots(double a, double b, double c, double squareRoot)
    {
        var q = -0.5 * (b + ScalarSign.CopySign(squareRoot, b));
        return q == 0 ? (0, 0) : (q / a, c / q);
    }

    internal static double? SelectStandardExplicitSagDistance(
        double first,
        double second,
        double originX,
        double originY,
        double originZ,
        double directionX,
        double directionY,
        double directionZ,
        double radius,
        double conic)
    {
        var firstDistance = ValidateStandardExplicitSagDistance(
            first,
            originX,
            originY,
            originZ,
            directionX,
            directionY,
            directionZ,
            radius,
            conic);
        var secondDistance = ValidateStandardExplicitSagDistance(
            second,
            originX,
            originY,
            originZ,
            directionX,
            directionY,
            directionZ,
            radius,
            conic);
        if (!firstDistance.HasValue)
        {
            return secondDistance;
        }

        if (!secondDistance.HasValue)
        {
            return firstDistance;
        }

        return Math.Min(firstDistance.Value, secondDistance.Value);
    }

    internal static double? ValidateStandardExplicitSagDistance(
        double? candidate,
        double originX,
        double originY,
        double originZ,
        double directionX,
        double directionY,
        double directionZ,
        double radius,
        double conic)
    {
        const double tolerance = 1e-12;
        if (candidate is not { } rawDistance || !double.IsFinite(rawDistance) || rawDistance < -tolerance)
        {
            return null;
        }

        var distance = Math.Max(0, rawDistance);
        var x = originX + (directionX * distance);
        var y = originY + (directionY * distance);
        var z = originZ + (directionZ * distance);
        var curvature = 1.0 / radius;
        var r2 = (x * x) + (y * y);
        var rootArgument = 1.0 - ((1.0 + conic) * curvature * curvature * r2);
        if (rootArgument < -tolerance)
        {
            return null;
        }

        var sag = curvature * r2 / (1.0 + Math.Sqrt(Math.Max(0, rootArgument)));
        var sagTolerance = 1e-8 * Math.Max(1.0, Math.Max(Math.Abs(z), Math.Abs(sag)));
        return Math.Abs(z - sag) <= sagTolerance ? distance : null;
    }
}
